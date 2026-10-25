using System.Runtime.InteropServices;
using System.Xml.Linq;
using Microsoft.Win32;

namespace SteppedOut.Tests;

[TestClass]
public class PackagedTests
{
    [TestMethod]
    public void OnlyANameThatDoesNotFitOrFitsMeansAPackage()
    {
        Assert.IsTrue(Packaged.FromResult(122), "ERROR_INSUFFICIENT_BUFFER: packaged, the name did not fit");
        Assert.IsTrue(Packaged.FromResult(0));
        Assert.IsFalse(Packaged.FromResult(Packaged.AppModelErrorNoPackage), "APPMODEL_ERROR_NO_PACKAGE: the normal exe");
        Assert.IsFalse(Packaged.FromResult(87), "any other answer keeps the normal exe's paths");
        Assert.IsFalse(Packaged.FromResult(-1));
    }

    [TestMethod]
    public void TheTestRunIsTheNormalExe()
    {
        Assert.IsFalse(Packaged.IsPackaged);
        Assert.IsNull(Packaged.FamilyName);
        Assert.IsNull(Packaged.StartupTask);
        Assert.AreEqual(AppInfo.ConfigDir, Packaged.SettingsFolder());
    }

    const string Local = @"X:\Profile\AppData\Local";
    const string Roaming = @"X:\Profile\AppData\Roaming\SteppedOut";
    const string Family = "12345Publisher.SteppedOut_abcd1234efgh5";
    const string Own = @"X:\Profile\AppData\Local\Packages\12345Publisher.SteppedOut_abcd1234efgh5\LocalCache\Roaming\SteppedOut";

    static string Folder(bool packaged, string? family, params string[] existing) =>
        Packaged.SettingsFolder(packaged, family, Local, Roaming, path => existing.Contains(path, StringComparer.OrdinalIgnoreCase));

    [TestMethod]
    public void TheNormalExeKeepsItsAppDataFolder()
    {
        Assert.AreEqual(Roaming, Folder(false, null));
        Assert.AreEqual(Roaming, Folder(false, Family, Own + @"\config.json"));
    }

    [TestMethod]
    public void APackageShowsItsOwnFolderUnlessItUsesTheNormalExesSettings()
    {
        Assert.AreEqual(Own, Folder(true, Family, Own, Own + @"\config.json"));
        // Windows shows the package its own copy first, so that one wins over the normal exe's.
        Assert.AreEqual(Own, Folder(true, Family, Own, Own + @"\config.json", Roaming, Roaming + @"\config.json"));
        // Only the normal exe's file: Windows lets the package change it in place, even when the
        // package has written other files, such as the log, to its own folder.
        Assert.AreEqual(Roaming, Folder(true, Family, Roaming, Roaming + @"\config.json"));
        Assert.AreEqual(Roaming, Folder(true, Family, Own, Own + @"\SteppedOut.log", Roaming, Roaming + @"\config.json"));
        // No settings saved yet, but the package has written its log: its own folder.
        Assert.AreEqual(Own, Folder(true, Family, Own, Own + @"\SteppedOut.log"));
        // Nothing written anywhere yet: the plain folder, never one Explorer can't open.
        Assert.AreEqual(Roaming, Folder(true, Family));
        Assert.AreEqual(Roaming, Folder(true, Family, Roaming));
        // A package whose family name could not be read keeps the plain folder.
        Assert.AreEqual(Roaming, Folder(true, null));
        Assert.AreEqual(Roaming, Folder(true, ""));
    }

    [TestMethod]
    public void EveryTaskStateMapsToARowState()
    {
        Assert.AreEqual(AutostartState.Off, Autostart.From(StartupTaskState.Disabled));
        Assert.AreEqual(AutostartState.OffInWindows, Autostart.From(StartupTaskState.DisabledByUser));
        Assert.AreEqual(AutostartState.On, Autostart.From(StartupTaskState.Enabled));
        Assert.AreEqual(AutostartState.OffByPolicy, Autostart.From(StartupTaskState.DisabledByPolicy));
        Assert.AreEqual(AutostartState.OnByPolicy, Autostart.From(StartupTaskState.EnabledByPolicy));
        Assert.AreEqual(AutostartState.Off, Autostart.From((StartupTaskState)7), "a state Windows adds later reads as off");
    }

    [TestMethod]
    public void OnlyOffAndOnCanBeChangedFromTheApp()
    {
        Assert.IsTrue(AutostartState.Off.CanChange());
        Assert.IsTrue(AutostartState.On.CanChange());
        Assert.IsFalse(AutostartState.OffInWindows.CanChange());
        Assert.IsFalse(AutostartState.OffByPolicy.CanChange());
        Assert.IsFalse(AutostartState.OnByPolicy.CanChange());

        Assert.IsTrue(AutostartState.On.IsOn());
        Assert.IsTrue(AutostartState.OnByPolicy.IsOn());
        Assert.IsFalse(AutostartState.Off.IsOn());
        Assert.IsFalse(AutostartState.OffInWindows.IsOn());
        Assert.IsFalse(AutostartState.OffByPolicy.IsOn());
    }

    [TestMethod]
    public void OnlyTheStatesWindowsHoldsGetALineUnderTheSwitch()
    {
        foreach (var text in new[] { EnglishV1.Text, ArabicV1.Text })
        {
            Assert.IsNull(AutostartState.Off.Note(text));
            Assert.IsNull(AutostartState.On.Note(text));
            Assert.AreEqual(text.StartupOffInWindows, AutostartState.OffInWindows.Note(text));
            Assert.AreEqual(text.StartupOffByPolicy, AutostartState.OffByPolicy.Note(text));
            Assert.AreEqual(text.StartupOnByPolicy, AutostartState.OnByPolicy.Note(text));
        }
        Assert.AreNotEqual(EnglishV1.Text.StartupOffInWindows, ArabicV1.Text.StartupOffInWindows);
    }

    // The raw calls only work if their slot numbers and interface ids are Windows' own. They are
    // read here from the metadata Windows ships in System32\WinMetadata, the same files a projection
    // is built from, so a typo can't hide until the Store version runs.
    [TestMethod]
    public void TheRawCallsMatchWindowsOwnMetadata()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WinMetadata");
        var model = Metadata.Read(Path.Combine(folder, "Windows.ApplicationModel.winmd"));
        var foundation = Metadata.Read(Path.Combine(folder, "Windows.Foundation.winmd"));

        var statics = model["Windows.ApplicationModel.IStartupTaskStatics"];
        Assert.AreEqual(StartupTaskApi.StaticsIid, statics.Guid);
        Assert.AreEqual(StartupTaskApi.GetAsyncSlot, statics.Slot("GetAsync"));

        var task = model["Windows.ApplicationModel.IStartupTask"];
        Assert.AreEqual(StartupTaskApi.RequestEnableAsyncSlot, task.Slot("RequestEnableAsync"));
        Assert.AreEqual(StartupTaskApi.DisableSlot, task.Slot("Disable"));
        Assert.AreEqual(StartupTaskApi.StateSlot, task.Slot("get_State"));
        Assert.IsTrue(model.ContainsKey(StartupTaskApi.ClassName));

        var info = foundation["Windows.Foundation.IAsyncInfo"];
        Assert.AreEqual(StartupTaskApi.AsyncInfoIid, info.Guid);
        Assert.AreEqual(StartupTaskApi.StatusSlot, info.Slot("get_Status"));
        Assert.AreEqual(StartupTaskApi.ErrorCodeSlot, info.Slot("get_ErrorCode"));
        Assert.AreEqual(StartupTaskApi.CancelSlot, info.Slot("Cancel"));
        Assert.AreEqual(StartupTaskApi.GetResultsSlot, foundation["Windows.Foundation.IAsyncOperation`1"].Slot("GetResults"));

        var states = model["Windows.ApplicationModel.StartupTaskState"].Values;
        foreach (var state in Enum.GetValues<StartupTaskState>())
        {
            Assert.AreEqual((int)state, states[state.ToString()], state.ToString());
        }
        Assert.HasCount(Enum.GetValues<StartupTaskState>().Length, states);
    }

    // Just enough of a winmd reader for the test above: each type's interface id, its methods in
    // vtable order (after IUnknown's and IInspectable's six), and an enum's values.
    sealed class Metadata
    {
        public Guid Guid { get; private init; }

        public List<string> Methods { get; } = new();

        public Dictionary<string, int> Values { get; } = new();

        public int Slot(string method)
        {
            int index = Methods.IndexOf(method);
            Assert.IsGreaterThanOrEqualTo(0, index, method + " not found");
            return 6 + index;
        }

        public static Dictionary<string, Metadata> Read(string path)
        {
            if (!File.Exists(path)) Assert.Inconclusive(path + " is not on this PC");
            using var stream = File.OpenRead(path);
            using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
            var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            var types = new Dictionary<string, Metadata>();
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                var found = new Metadata { Guid = GuidOf(reader, type) };
                foreach (var method in type.GetMethods()) found.Methods.Add(reader.GetString(reader.GetMethodDefinition(method).Name));
                foreach (var fieldHandle in type.GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    var constant = field.GetDefaultValue();
                    if (!constant.IsNil) found.Values[reader.GetString(field.Name)] = reader.GetBlobReader(reader.GetConstant(constant).Value).ReadInt32();
                }
                types[reader.GetString(type.Namespace) + "." + reader.GetString(type.Name)] = found;
            }
            return types;
        }

        static Guid GuidOf(System.Reflection.Metadata.MetadataReader reader, System.Reflection.Metadata.TypeDefinition type)
        {
            foreach (var handle in type.GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != System.Reflection.Metadata.HandleKind.MemberReference) continue;
                var constructor = reader.GetMemberReference((System.Reflection.Metadata.MemberReferenceHandle)attribute.Constructor);
                if (constructor.Parent.Kind != System.Reflection.Metadata.HandleKind.TypeReference) continue;
                var owner = reader.GetTypeReference((System.Reflection.Metadata.TypeReferenceHandle)constructor.Parent);
                if (reader.GetString(owner.Name) != "GuidAttribute") continue;
                var blob = reader.GetBlobReader(attribute.Value);
                blob.ReadUInt16();
                int a = blob.ReadInt32();
                short b = blob.ReadInt16();
                short c = blob.ReadInt16();
                var rest = new byte[8];
                for (int i = 0; i < rest.Length; i++) rest[i] = blob.ReadByte();
                return new Guid(a, b, c, rest);
            }
            return Guid.Empty;
        }
    }

    [TestMethod]
    [DataRow((int)StartupTaskState.Disabled, (int)AutostartState.Off)]
    [DataRow((int)StartupTaskState.DisabledByUser, (int)AutostartState.OffInWindows)]
    [DataRow((int)StartupTaskState.Enabled, (int)AutostartState.On)]
    [DataRow((int)StartupTaskState.DisabledByPolicy, (int)AutostartState.OffByPolicy)]
    [DataRow((int)StartupTaskState.EnabledByPolicy, (int)AutostartState.OnByPolicy)]
    public void ReadAsksTheTaskAndNeverTheRunKey(int state, int expected)
    {
        WithKey(key =>
        {
            // An entry for this very copy, as the normal exe would leave it: the package ignores it.
            using (var run = Registry.CurrentUser.CreateSubKey(key)) run.SetValue(AppInfo.Name, Autostart.Command());
            var task = new FakeTask((StartupTaskState)state);
            Assert.AreEqual((AutostartState)expected, Autostart.Read(task, key));
            CollectionAssert.AreEqual(new[] { "State" }, task.Calls);
        });
    }

    [TestMethod]
    public void TurningOnRequestsTheTask()
    {
        var task = new FakeTask(StartupTaskState.Disabled);
        Assert.AreEqual(AutostartState.On, Autostart.Choose(true, task, NoKey));
        CollectionAssert.AreEqual(new[] { "State", "RequestEnable" }, task.Calls);
    }

    [TestMethod]
    public void WindowsCanStillRefuseTheRequest()
    {
        // Turned off in Task Manager between reading and asking: the request leaves it off.
        var task = new FakeTask(StartupTaskState.Disabled) { AfterRequest = StartupTaskState.DisabledByUser };
        Assert.AreEqual(AutostartState.OffInWindows, Autostart.Choose(true, task, NoKey));
        task = new FakeTask(StartupTaskState.Disabled) { AfterRequest = StartupTaskState.DisabledByPolicy };
        Assert.AreEqual(AutostartState.OffByPolicy, Autostart.Choose(true, task, NoKey));
    }

    [TestMethod]
    public void TurningOffDisablesTheTask()
    {
        var task = new FakeTask(StartupTaskState.Enabled);
        Assert.AreEqual(AutostartState.Off, Autostart.Choose(false, task, NoKey));
        CollectionAssert.AreEqual(new[] { "State", "Disable", "State" }, task.Calls);
    }

    [TestMethod]
    [DataRow((int)StartupTaskState.DisabledByUser, true, (int)AutostartState.OffInWindows)]
    [DataRow((int)StartupTaskState.DisabledByUser, false, (int)AutostartState.OffInWindows)]
    [DataRow((int)StartupTaskState.DisabledByPolicy, true, (int)AutostartState.OffByPolicy)]
    [DataRow((int)StartupTaskState.DisabledByPolicy, false, (int)AutostartState.OffByPolicy)]
    [DataRow((int)StartupTaskState.EnabledByPolicy, true, (int)AutostartState.OnByPolicy)]
    [DataRow((int)StartupTaskState.EnabledByPolicy, false, (int)AutostartState.OnByPolicy)]
    public void AStateWindowsHoldsIsLeftAsItIs(int state, bool on, int expected)
    {
        var task = new FakeTask((StartupTaskState)state);
        Assert.AreEqual((AutostartState)expected, Autostart.Choose(on, task, NoKey));
        CollectionAssert.AreEqual(new[] { "State" }, task.Calls, "nothing is asked of a task Windows holds");
    }

    [TestMethod]
    public void ChoosingWhatItAlreadyIsDoesNothing()
    {
        var on = new FakeTask(StartupTaskState.Enabled);
        Assert.AreEqual(AutostartState.On, Autostart.Choose(true, on, NoKey));
        CollectionAssert.AreEqual(new[] { "State" }, on.Calls);
        var off = new FakeTask(StartupTaskState.Disabled);
        Assert.AreEqual(AutostartState.Off, Autostart.Choose(false, off, NoKey));
        CollectionAssert.AreEqual(new[] { "State" }, off.Calls);
    }

    [TestMethod]
    public void AFailingTaskReadsAsItIsAndNeverThrows()
    {
        Assert.AreEqual(AutostartState.Off, Autostart.Read(new FakeTask(StartupTaskState.Enabled) { FailOn = "State" }, NoKey));
        Assert.AreEqual(AutostartState.Off, Autostart.Choose(true, new FakeTask(StartupTaskState.Disabled) { FailOn = "State" }, NoKey));

        // The switch then shows what Windows has, not what was asked for.
        Assert.AreEqual(AutostartState.Off, Autostart.Choose(true, new FakeTask(StartupTaskState.Disabled) { FailOn = "RequestEnable" }, NoKey));
        Assert.AreEqual(AutostartState.On, Autostart.Choose(false, new FakeTask(StartupTaskState.Enabled) { FailOn = "Disable" }, NoKey));
        Assert.AreEqual(AutostartState.Off, Autostart.Choose(false, new FakeTask(StartupTaskState.Enabled) { Failure = new TimeoutException("hung") }, NoKey));
    }

    [TestMethod]
    public void APackageNeverWritesTheRunKey()
    {
        WithKey(key =>
        {
            Autostart.Choose(true, new FakeTask(StartupTaskState.Disabled), key);
            using (var run = Registry.CurrentUser.OpenSubKey(key)) Assert.IsNull(run?.GetValue(AppInfo.Name));

            // An entry another copy left is not the package's to remove either.
            using (var run = Registry.CurrentUser.CreateSubKey(key)) run.SetValue(AppInfo.Name, @"""X:\Elsewhere\SteppedOut.exe""");
            Autostart.Choose(false, new FakeTask(StartupTaskState.Enabled), key);
            using (var run = Registry.CurrentUser.OpenSubKey(key)) Assert.AreEqual(@"""X:\Elsewhere\SteppedOut.exe""", run?.GetValue(AppInfo.Name));
            using (var approved = Registry.CurrentUser.OpenSubKey(Autostart.ApprovedKey(key))) Assert.IsNull(approved, "no off flag is written for a package either");
        });
    }

    // The real call, from a process with no package: Windows refuses, and the switch reads as off
    // within its time limit instead of hanging or crashing. A package is needed to see it work.
    [TestMethod]
    public void TheRealTaskOutsideAPackageFailsCleanly()
    {
        var api = new StartupTaskApi();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Exception? failure = null;
        try
        {
            api.State();
        }
        catch (Exception e)
        {
            failure = e;
        }
        Assert.IsNotNull(failure, "a process with no package has no startup task");
        Assert.IsTrue(failure is COMException or ArgumentException or UnauthorizedAccessException or InvalidOperationException
            or FileNotFoundException, "unexpected failure: " + failure);
        Assert.AreEqual(AutostartState.Off, Autostart.Read(api, NoKey));
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(6), "took " + watch.Elapsed);
    }

    [TestMethod]
    public void TheManifestDeclaresTheTaskTheAppAsksFor()
    {
        XNamespace desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
        var manifest = XDocument.Load(Path.Combine(Root, "tools", "store", "Package.appxmanifest.template"));
        var extension = manifest.Descendants(desktop + "Extension").Single(e => (string?)e.Attribute("Category") == "windows.startupTask");
        var task = extension.Element(desktop + "StartupTask")!;
        Assert.AreEqual(StartupTaskApi.TaskId, (string?)task.Attribute("TaskId"));
        Assert.AreEqual("false", (string?)task.Attribute("Enabled"), "off until the person turns it on, like the normal exe");
        Assert.AreEqual(AppInfo.Name + ".exe", (string?)extension.Attribute("Executable"));
        Assert.AreEqual("Windows.FullTrustApplication", (string?)extension.Attribute("EntryPoint"));
    }

    // A key that is never created: the Run key is not looked at once there is a task.
    const string NoKey = @"Software\SteppedOut.PackagedTests-unused\Run";

    // Each test gets its own top-level scratch key and deletes only that tree, since the tests in
    // this class run side by side: a shared parent could be deleted under another test.
    static void WithKey(Action<string> test)
    {
        string root = @"Software\SteppedOut.PackagedTests-" + Guid.NewGuid().ToString("N");
        try
        {
            test(root + @"\Run");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
        }
    }

    static string Root
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "tools", "store", "Package.appxmanifest.template"))) return dir.FullName;
            }
            throw new InvalidOperationException("tools/store/Package.appxmanifest.template not found above the test folder");
        }
    }

    sealed class FakeTask(StartupTaskState state) : IStartupTask
    {
        StartupTaskState current = state;

        public StartupTaskState AfterRequest { get; init; } = StartupTaskState.Enabled;

        public string? FailOn { get; init; }

        public Exception? Failure { get; init; }

        public List<string> Calls { get; } = new();

        public StartupTaskState State()
        {
            Step("State");
            return current;
        }

        public StartupTaskState RequestEnable()
        {
            Step("RequestEnable");
            current = AfterRequest;
            return current;
        }

        public void Disable()
        {
            Step("Disable");
            current = StartupTaskState.Disabled;
        }

        void Step(string name)
        {
            Calls.Add(name);
            if (Failure is not null) throw Failure;
            if (FailOn == name) throw new COMException("the startup task failed", unchecked((int)0x80004005));
        }
    }
}
