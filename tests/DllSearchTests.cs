using System.Reflection;
using System.Runtime.InteropServices;

namespace SteppedOut.Tests;

[TestClass]
public class DllSearchTests
{
    static readonly Assembly App = typeof(AppInfo).Assembly;

    // A DLL dropped next to the exe is never loaded in place of Windows' own.
    [TestMethod]
    public void TheAppLoadsItsDllsOnlyFromSystem32()
    {
        var search = App.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();
        Assert.IsNotNull(search, "the assembly sets where its DLLs are looked for");
        Assert.AreEqual(DllImportSearchPath.System32, search.Paths);
    }

    // Each import is a bare Windows DLL name found in System32, and none asks for another folder.
    [TestMethod]
    public void EveryImportIsAWindowsDllAndNoneLooksElsewhere()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var imports = App.GetTypes().SelectMany(type => type.GetMethods(all))
            .Select(method => (Method: method, Import: method.GetCustomAttribute<DllImportAttribute>()))
            .Where(found => found.Import is not null)
            .ToList();
        Assert.IsGreaterThan(20, imports.Count, "the imports were found");
        foreach (var (method, import) in imports)
        {
            string where = $"{method.DeclaringType!.Name}.{method.Name}";
            string dll = import!.Value;
            Assert.AreEqual(Path.GetFileName(dll), dll, where + " names its DLL without a folder");
            string file = dll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? dll : dll + ".dll";
            Assert.IsTrue(File.Exists(Path.Combine(Environment.SystemDirectory, file)), $"{where}: {dll} is in System32");
            Assert.IsNull(method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>(), where + " keeps the assembly's search");
            Assert.IsNull(method.DeclaringType.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>(), where + " keeps the assembly's search");
        }
    }
}
