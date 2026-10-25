using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

// 在独立进程内载入真实依赖并安装所有补丁，不启动游戏或读取玩家存档。
if (args.Length != 3)
{
    Console.Error.WriteLine("用法：CompatibilityAudit <游戏程序集目录> <Mod DLL> <BaseLib目录>；必须使用 .NET 9 运行。");
    Environment.ExitCode = 2;
    return;
}
string gameDir = Path.GetFullPath(args[0]);
string modPath = Path.GetFullPath(args[1]);
string baseLibDir = Path.GetFullPath(args[2]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (string dir in new[] { gameDir, baseLibDir, Path.GetDirectoryName(modPath)! })
    {
        string candidate = Path.Combine(dir, name.Name + ".dll");
        if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
    }
    return null;
};
Assembly mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(modPath);
int failures = 0;
Harmony harmony = new("TheBookOfAges.CompatibilityAudit");
foreach (Type type in mod.GetTypes().Where(t => t.GetCustomAttributes<HarmonyPatch>().Any()))
{
    try
    {
        var methods = harmony.CreateClassProcessor(type).Patch();
        if (methods == null || methods.Count == 0)
        {
            throw new MissingMethodException("该补丁没有安装到任何目标方法。");
        }
        Console.WriteLine($"通过 {type.Name}: {string.Join("; ", methods?.Select(m => m.Name) ?? [])}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"失败 {type.Name}: {ex}");
    }
}
Console.WriteLine($"补丁检查完成，失败数={failures}");
Environment.ExitCode = failures == 0 ? 0 : 1;
