extends SceneTree
## 在空白项目中挂载成品，确保图标和本地化真实来自 PCK，而非开发目录。

func _initialize() -> void:
    var args := OS.get_cmdline_user_args()
    if args.size() != 2 or not ProjectSettings.load_resource_pack(args[0]):
        push_error("资源包挂载失败。")
        quit(1)
        return
    var paths = JSON.parse_string(FileAccess.get_file_as_string(args[1]))
    if not paths is Array or paths.is_empty():
        push_error("资源清单为空或格式错误。")
        quit(1)
        return
    for path in paths:
        if str(path).ends_with(".png"):
            var texture = load(path)
            if not texture is Texture2D or texture.get_width() <= 0:
                push_error("图标不可用：" + str(path))
                quit(1)
                return
        elif JSON.parse_string(FileAccess.get_file_as_string(path)) == null:
            push_error("本地化资源不可用：" + str(path))
            quit(1)
            return
    print("GM_PACK_VERIFIED ", paths.size())
    quit(0)
