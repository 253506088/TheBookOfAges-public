using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 刷怪笼模块分文件：怪物按钮、怪物预览（NCreatureVisuals）的尺寸自适应、视口避让与动画启动。
/// 内容来自 GmUiManager.cs 的怪物相关方法段，行为与原实现一致，只做物理拆分。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 刷新刷怪笼页的网格内容。
    /// </summary>
    private static void RefreshMonsterGrid()
    {
        if (_grid == null || _searchInput == null || _categoryFilter == null || _sourceFilter == null)
        {
            return;
        }

        IReadOnlyList<MonsterDisplayInfo> monsters = MonsterService.GetMonsters(
            _searchInput.Text,
            GetSelectedFilterKey(_categoryFilter),
            GetSelectedFilterKey(_sourceFilter));
        UpdateGridColumns();

        ClearGridChildren();

        foreach (MonsterDisplayInfo monster in monsters)
        {
            _grid.AddChild(CreateMonsterButton(monster));
        }

        UpdateSelectionStatus(monsters.Count);
        UpdateConfirmButtonAvailability();
    }

    /// <summary>
    /// 创建单个怪物按钮；列表只显示轻量文字信息，动态预览放到悬浮框里懒加载。
    /// </summary>
    private static Button CreateMonsterButton(MonsterDisplayInfo monster)
    {
        Button button = new()
        {
            CustomMinimumSize = new Vector2(178, 126),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            ClipContents = true,
        };

        VBoxContainer layout = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        layout.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        Label nameLabel = new()
        {
            Text = monster.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        nameLabel.Name = "MonsterNameLabel";
        nameLabel.AddThemeColorOverride("font_color", new Color(1f, 0.84f, 0f, 1f));
        layout.AddChild(nameLabel);

        Label tierLabel = new()
        {
            Text = monster.TierText,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        tierLabel.Name = "MonsterTierLabel";
        tierLabel.AddThemeColorOverride("font_color", new Color(0.5f, 1f, 0.5f, 1f));
        layout.AddChild(tierLabel);

        Label hpLabel = new()
        {
            Text = GmUiText.MonsterHpRange(monster.MinHp, monster.MaxHp),
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        hpLabel.Name = "MonsterHpLabel";
        hpLabel.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.45f, 1f));
        layout.AddChild(hpLabel);

        Label selectedLabel = new()
        {
            Text = BuildMonsterButtonText(monster),
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = SelectedSpawnMonsters.ContainsKey(monster.IdText),
        };
        selectedLabel.Name = "MonsterSelectedLabel";
        layout.AddChild(selectedLabel);

        button.AddChild(layout);
        button.Modulate = GetMonsterButtonColor(monster);
        RefreshMonsterButtonVisual(button, monster);
        RegisterMonsterTooltip(button, monster);
        button.Pressed += () => OnMonsterButtonPressed(monster, button);
        return button;
    }

    /// <summary>
    /// 尝试创建怪物动态预览控件；失败时返回空，并把该怪物加入失败缓存避免重复尝试。
    /// 使用 ToMutable 副本，避免规范实例在预览阶段被错误改写。
    /// </summary>
    private static Control? TryCreateMonsterPreviewControl(MonsterDisplayInfo monster, Vector2 previewSize)
    {
        if (MonsterPreviewDisabledIds.Contains(monster.IdText))
        {
            return null;
        }

        try
        {
            int previewViewportWidth = Math.Max(1, (int)previewSize.X);
            int previewViewportHeight = Math.Max(1, (int)previewSize.Y);
            SubViewportContainer container = new()
            {
                CustomMinimumSize = new Vector2(previewViewportWidth, previewViewportHeight),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Stretch = true,
            };

            SubViewport viewport = new()
            {
                Size = new Vector2I(previewViewportWidth, previewViewportHeight),
                TransparentBg = true,
                Disable3D = true,
            };
            container.AddChild(viewport);

            Node2D stage = new();
            viewport.AddChild(stage);

            var previewMonster = monster.Monster.ToMutable();
            NCreatureVisuals preview = previewMonster.CreateVisuals();
            stage.AddChild(preview);

            FitMonsterPreview(preview, new Vector2(previewViewportWidth, previewViewportHeight));
            ScheduleMonsterPreviewRefit(monster, preview, viewport, new Vector2(previewViewportWidth, previewViewportHeight));
            TryStartMonsterPreviewAnimation(monster, preview);
            return container;
        }
        catch (Exception ex)
        {
            MonsterPreviewDisabledIds.Add(monster.IdText);
            MainFile.Logger.Info($"刷怪笼创建怪物悬浮预览失败，后续改用纯文字提示：名称={monster.Name}，ID={monster.IdText}，异常={ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 根据怪物可视节点的默认缩放和边界进行摆放：
    /// 默认保留战斗房间原比例，只在超出预览框时缩小，避免无条件缩略。
    /// </summary>
    private static void FitMonsterPreview(NCreatureVisuals preview, Vector2 viewportSize)
    {
        float targetScale = preview.DefaultScale > 0f ? preview.DefaultScale : 1f;
        Vector2 boundsSize = preview.Bounds?.Size ?? Vector2.Zero;
        Vector2 boundsPosition = preview.Bounds?.Position ?? Vector2.Zero;
        float previewPadding = MonsterPreviewContentPadding;

        if (boundsSize.X > 0f && boundsSize.Y > 0f)
        {
            float safeWidth = Math.Max(1f, viewportSize.X - previewPadding * 2f);
            float safeHeight = Math.Max(1f, viewportSize.Y - previewPadding * 2f);
            float limitScale = Math.Min(safeWidth / boundsSize.X, safeHeight / boundsSize.Y);
            if (float.IsFinite(limitScale) && limitScale > 0f && limitScale < 1f)
            {
                targetScale *= limitScale;
            }
        }
        else
        {
            targetScale *= 0.8f;
        }

        try
        {
            preview.SetScaleAndHue(targetScale, 0f);
        }
        catch
        {
            preview.Scale = new Vector2(targetScale, targetScale);
        }

        Vector2 anchor = new(viewportSize.X * 0.5f, viewportSize.Y * 0.5f);
        if (boundsSize.X > 0f && boundsSize.Y > 0f)
        {
            Vector2 boundsCenter = boundsPosition + boundsSize * 0.5f;
            preview.Position = anchor - boundsCenter * targetScale;
            KeepPreviewInsideViewport(preview, boundsPosition, boundsSize, targetScale, viewportSize, previewPadding);

            float forceFitScale = Math.Min(
                Math.Max(1f, viewportSize.X - previewPadding * 2f) / boundsSize.X,
                Math.Max(1f, viewportSize.Y - previewPadding * 2f) / boundsSize.Y);
            if (float.IsFinite(forceFitScale) && forceFitScale > 0f && targetScale > forceFitScale)
            {
                targetScale = forceFitScale;
                try
                {
                    preview.SetScaleAndHue(targetScale, 0f);
                }
                catch
                {
                    preview.Scale = new Vector2(targetScale, targetScale);
                }

                preview.Position = anchor - boundsCenter * targetScale;
                KeepPreviewInsideViewport(preview, boundsPosition, boundsSize, targetScale, viewportSize, previewPadding);
            }

            return;
        }

        preview.Position = new Vector2(viewportSize.X * 0.5f, viewportSize.Y * 0.5f);
    }

    /// <summary>
    /// 对资源晚加载的怪物预览做延迟重排，避免首帧 Bounds 不稳定导致裁切。
    /// </summary>
    private static void ScheduleMonsterPreviewRefit(MonsterDisplayInfo monster, NCreatureVisuals preview, SubViewport viewport, Vector2 viewportSize)
    {
        foreach (float delay in MonsterPreviewRefitDelaySeconds)
        {
            _ = RefitMonsterPreviewAfterDelayAsync(monster, preview, viewport, viewportSize, delay);
        }
    }

    /// <summary>
    /// 延迟等待后重新适配怪物预览，并按实际渲染像素做二次校正。
    /// </summary>
    private static async Task RefitMonsterPreviewAfterDelayAsync(MonsterDisplayInfo monster, NCreatureVisuals preview, SubViewport viewport, Vector2 viewportSize, float delaySeconds)
    {
        try
        {
            if (_root == null || !GodotObject.IsInstanceValid(_root))
            {
                return;
            }

            SceneTree? tree = _root.GetTree();
            if (tree == null)
            {
                return;
            }

            await _root.ToSignal(tree.CreateTimer(delaySeconds), SceneTreeTimer.SignalName.Timeout);
            await _root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            if (!GodotObject.IsInstanceValid(preview) ||
                !GodotObject.IsInstanceValid(viewport) ||
                preview.GetTree() == null)
            {
                return;
            }

            FitMonsterPreview(preview, viewportSize);
            TryAutoFitMonsterPreviewByRenderedPixels(monster, preview, viewport, viewportSize);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"刷怪笼怪物预览延迟重排失败：名称={monster.Name}，ID={monster.IdText}，异常={ex.Message}");
        }
    }

    /// <summary>
    /// 用 SubViewport 实际渲染出的非透明像素范围二次校正位置和缩放。
    /// 解决部分怪物 Bounds 与真实显示不一致导致的裁切问题。
    /// </summary>
    private static void TryAutoFitMonsterPreviewByRenderedPixels(MonsterDisplayInfo monster, NCreatureVisuals preview, SubViewport viewport, Vector2 viewportSize)
    {
        try
        {
            Texture2D? texture = viewport.GetTexture();
            if (texture == null)
            {
                return;
            }

            Image image = texture.GetImage();
            if (image == null || image.IsEmpty() || image.IsInvisible())
            {
                return;
            }

            Rect2I usedRect = image.GetUsedRect();
            if (usedRect.Size.X <= 0 || usedRect.Size.Y <= 0)
            {
                return;
            }

            float safeWidth = Math.Max(1f, viewportSize.X - MonsterPreviewContentPadding * 2f);
            float safeHeight = Math.Max(1f, viewportSize.Y - MonsterPreviewContentPadding * 2f);
            float fitScale = Math.Min(safeWidth / usedRect.Size.X, safeHeight / usedRect.Size.Y);
            if (!float.IsFinite(fitScale) || fitScale <= 0f)
            {
                return;
            }

            float currentScale = preview.Scale.X;
            float nextScale = currentScale * fitScale;
            if (!float.IsFinite(nextScale) || nextScale <= 0f)
            {
                return;
            }

            try
            {
                preview.SetScaleAndHue(nextScale, 0f);
            }
            catch
            {
                preview.Scale = new Vector2(nextScale, nextScale);
            }

            Vector2 usedCenter = new(
                usedRect.Position.X + usedRect.Size.X * 0.5f,
                usedRect.Position.Y + usedRect.Size.Y * 0.5f);
            Vector2 targetCenter = viewportSize * 0.5f;
            preview.Position += targetCenter - usedCenter;

            MainFile.Logger.Info($"刷怪笼怪物预览像素校正：名称={monster.Name}，ID={monster.IdText}，像素包围={usedRect}，倍率={fitScale:0.###}。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"刷怪笼怪物预览像素校正失败：名称={monster.Name}，ID={monster.IdText}，异常={ex.Message}");
        }
    }

    /// <summary>
    /// 预览定位后再做一次边界回推，尽量保证整只怪物都在可视区域内。
    /// </summary>
    private static void KeepPreviewInsideViewport(NCreatureVisuals preview, Vector2 boundsPosition, Vector2 boundsSize, float scale, Vector2 viewportSize, float padding)
    {
        Vector2 topLeft = preview.Position + boundsPosition * scale;
        Vector2 bottomRight = preview.Position + (boundsPosition + boundsSize) * scale;
        Vector2 offset = Vector2.Zero;

        if (topLeft.X < padding)
        {
            offset.X += padding - topLeft.X;
        }
        if (bottomRight.X > viewportSize.X - padding)
        {
            offset.X -= bottomRight.X - (viewportSize.X - padding);
        }

        if (topLeft.Y < padding)
        {
            offset.Y += padding - topLeft.Y;
        }
        if (bottomRight.Y > viewportSize.Y - padding)
        {
            offset.Y -= bottomRight.Y - (viewportSize.Y - padding);
        }

        preview.Position += offset;
    }

    /// <summary>
    /// 尝试让怪物预览播放待机动画。
    /// 优先使用 idle，失败时回退到其他常见待机动画名，并记录日志方便排查。
    /// </summary>
    private static void TryStartMonsterPreviewAnimation(MonsterDisplayInfo monster, NCreatureVisuals preview)
    {
        try
        {
            if (!preview.HasSpineAnimation || !preview.SpineAnimation.IsValid)
            {
                MainFile.Logger.Info($"刷怪笼怪物预览未启用 Spine 动画，保持静态显示：名称={monster.Name}，ID={monster.IdText}。");
                return;
            }

            preview.SpineAnimation.SetTimeScale(1f);
            foreach (string animationName in MonsterPreviewAnimationCandidates)
            {
                try
                {
                    preview.SpineAnimation.SetAnimation(animationName, true, 0);
                    MainFile.Logger.Info($"刷怪笼怪物预览动画播放成功：名称={monster.Name}，ID={monster.IdText}，动画={animationName}。");
                    return;
                }
                catch
                {
                }
            }

            MainFile.Logger.Info($"刷怪笼怪物预览未找到可用待机动画，保持默认状态：名称={monster.Name}，ID={monster.IdText}。");
        }
        catch
        {
            MainFile.Logger.Info($"刷怪笼怪物预览播放动画失败，保持默认状态：名称={monster.Name}，ID={monster.IdText}。");
        }
    }

    /// <summary>
    /// 处理怪物按钮点击：第一次选中，再次点击取消，支持多选。
    /// </summary>
    private static void OnMonsterButtonPressed(MonsterDisplayInfo monster, Button button)
    {
        if (!SelectedSpawnMonsters.Remove(monster.IdText))
        {
            SelectedSpawnMonsters[monster.IdText] = monster;
        }

        UpdateMonsterButtonText(button, monster);
        button.Modulate = GetMonsterButtonColor(monster);
        RefreshMonsterButtonVisual(button, monster);
        UpdateSelectionStatus();
    }

    /// <summary>
    /// 更新怪物按钮内的名称标签文本。
    /// </summary>
    private static void UpdateMonsterButtonText(Button button, MonsterDisplayInfo monster)
    {
        Label? label = button.GetNodeOrNull<Label>("VBoxContainer/MonsterSelectedLabel");
        if (label != null)
        {
            label.Text = BuildMonsterButtonText(monster);
            label.Visible = !string.IsNullOrWhiteSpace(label.Text);
        }
    }

    /// <summary>
    /// 拼接怪物按钮选中状态文本。
    /// </summary>
    private static string BuildMonsterButtonText(MonsterDisplayInfo monster)
    {
        if (SelectedSpawnMonsters.ContainsKey(monster.IdText))
        {
            return GmUiText.Pick("已选", "Selected");
        }

        return string.Empty;
    }

    /// <summary>
    /// 拼接怪物按钮悬停提示。
    /// </summary>
    private static string BuildMonsterTooltip(MonsterDisplayInfo monster)
    {
        string safetyHint = MonsterService.GetMonsterRoomMismatchHint(monster);
        if (string.IsNullOrWhiteSpace(safetyHint))
        {
            return $"{monster.Name}\n{monster.TierText}\n{monster.Source}\n{GmUiText.MonsterHpRange(monster.MinHp, monster.MaxHp)}\nID: {monster.IdText}";
        }

        string modeHint = (_monsterRiskySummonCheckbox?.ButtonPressed ?? false)
            ? GmUiText.MonsterRiskyModeTooltip
            : GmUiText.MonsterSafeModeTooltip;
        return $"{monster.Name}\n{monster.TierText}\n{monster.Source}\n{GmUiText.MonsterHpRange(monster.MinHp, monster.MaxHp)}\nID: {monster.IdText}\n{safetyHint}\n{modeHint}";
    }

    /// <summary>
    /// 根据怪物选择状态设置按钮颜色。
    /// </summary>
    private static Color GetMonsterButtonColor(MonsterDisplayInfo monster)
    {
        if (!(_monsterRiskySummonCheckbox?.ButtonPressed ?? false) && !MonsterService.IsMonsterRecommendedInCurrentRoom(monster))
        {
            return new Color(1f, 0.74f, 0.32f, 0.82f);
        }

        return Colors.White;
    }
}
