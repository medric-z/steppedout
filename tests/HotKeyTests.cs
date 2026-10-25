using System.Runtime.InteropServices;

namespace SteppedOut.Tests;

// Only Ctrl+Alt+Shift+F24, a key no keyboard has, is ever registered for real here, and it is
// always unregistered again. Every other test registers through a fake, so the real Ctrl+Alt+L is
// never taken. Nothing here sends a keystroke.
[TestClass]
public class HotKeyTests
{
    const string TestKey = "Ctrl+Alt+Shift+F24";
    static readonly IntPtr English = new(0x04090409);
    static readonly IntPtr Polish = new(0x04150415);
    static readonly IntPtr Arabic = new(0x04010401);
    static readonly IntPtr German = new(0x04070407);

    // Layouts as a list, with what Ctrl+Alt (AltGr) and a key type on each, as on the real ones:
    // Polish types ł with AltGr+L and Ł with Shift; German types @ with AltGr+Q.
    sealed class FakeLayouts : IKeyboardLayouts
    {
        public List<IntPtr> List = new();
        public int Asked;

        public IReadOnlyList<IntPtr> Installed() => List.ToList();

        public string Typed(IntPtr layout, Shortcut s)
        {
            Asked++;
            bool altGr = s.HasCtrl && s.HasAlt;
            if (!altGr) return "";
            if (layout == Polish && s.Key == Keys.L) return s.HasShift ? "Ł" : "ł";
            if (layout == Polish && s.Key == Keys.A) return s.HasShift ? "Ą" : "ą";
            if (layout == German && s.Key == Keys.Q) return "@";
            return "";
        }
    }

    sealed class FakeApi : IHotKeyApi
    {
        public readonly List<(uint Modifiers, uint Key)> Registered = new();
        public int Registers;
        public int Unregisters;
        public int Error;
        public bool Throw;

        public int Register(IntPtr window, int id, uint modifiers, uint virtualKey)
        {
            if (Throw) throw new InvalidOperationException("boom");
            Registers++;
            if (Error != 0) return Error;
            Registered.Add((modifiers, virtualKey));
            return 0;
        }

        public void Unregister(IntPtr window, int id)
        {
            Unregisters++;
            if (Registered.Count > 0) Registered.RemoveAt(Registered.Count - 1);
        }
    }

    static (HotKey Key, FakeLayouts Layouts, FakeApi Api) Make(params IntPtr[] layouts)
    {
        var l = new FakeLayouts();
        l.List.AddRange(layouts);
        var a = new FakeApi();
        return (new HotKey(l, a), l, a);
    }

    [TestMethod]
    public void PolishLayoutStopsCtrlAltLEvenWhenEnglishIsInUse()
    {
        var (key, layouts, api) = Make(English, Polish);
        using (key)
        {
            Assert.AreEqual(HotKeyResult.TypesALetter, key.Apply("Ctrl+Alt+L"));
            Assert.AreEqual(0, api.Registers, "nothing may be registered");
            Assert.IsFalse(key.IsRegistered);
            Assert.AreEqual(HotKeyResult.TypesALetter, key.Result);
            Assert.AreEqual("04150415", key.TypingLayout);
            Assert.AreEqual(HotKeyResult.TypesALetter, key.Apply("Ctrl+Alt+Shift+L"));
            Assert.AreEqual(0, api.Registers);
        }
    }

    [TestMethod]
    public void EveryLayoutIsChecked()
    {
        // The layout that types it is last in the list.
        var (key, layouts, api) = Make(English, Arabic, German);
        using (key)
        {
            Assert.AreEqual(HotKeyResult.TypesALetter, key.Apply("Ctrl+Alt+Q"));
            Assert.AreEqual(3, layouts.Asked);
            Assert.AreEqual(0, api.Registers);
        }
    }

    [TestMethod]
    public void WithOnlyEnglishCtrlAltLIsRegisteredOnce()
    {
        var (key, _, api) = Make(English, Arabic);
        using (key)
        {
            Assert.AreEqual(HotKeyResult.Ok, key.Apply("Ctrl+Alt+L"));
            Assert.IsTrue(key.IsRegistered);
            Assert.HasCount(1, api.Registered);
            // MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, and VK_L.
            Assert.AreEqual((0x2u | 0x1u | 0x4000u, (uint)'L'), api.Registered[0]);

            // The same shortcut again checks the layouts but keeps the registration.
            Assert.AreEqual(HotKeyResult.Ok, key.Recheck());
            Assert.AreEqual(HotKeyResult.Ok, key.Apply("ctrl+alt+l"));
            Assert.AreEqual(1, api.Registers);
            Assert.AreEqual(0, api.Unregisters);
        }
        Assert.AreEqual(1, api.Unregisters, "disposing unregisters");
        Assert.IsEmpty(api.Registered);
    }

    [TestMethod]
    public void ALayoutAddedLaterTurnsTheShortcutOffOnRecheck()
    {
        var (key, layouts, api) = Make(English);
        using (key)
        {
            int changes = 0;
            key.ResultChanged += () => changes++;
            Assert.AreEqual(HotKeyResult.Ok, key.Apply("Ctrl+Alt+L"));
            Assert.AreEqual(1, changes, "the shortcut changed from none");
            layouts.List.Add(Polish);
            Assert.AreEqual(HotKeyResult.TypesALetter, key.Recheck());
            Assert.IsFalse(key.IsRegistered);
            Assert.AreEqual(1, api.Unregisters);
            Assert.IsEmpty(api.Registered);
            Assert.AreEqual(2, changes);

            // Polish removed again: it comes back on.
            layouts.List.Remove(Polish);
            Assert.AreEqual(HotKeyResult.Ok, key.Recheck());
            Assert.IsTrue(key.IsRegistered);
            Assert.AreEqual(3, changes);
        }
    }

    [TestMethod]
    public void TakenByAnotherAppIsInUseAndIsTriedAgain()
    {
        var (key, _, api) = Make(English);
        using (key)
        {
            api.Error = 1409;
            Assert.AreEqual(HotKeyResult.InUse, key.Apply("Ctrl+Alt+L"));
            Assert.IsFalse(key.IsRegistered);

            // The other app let it go.
            api.Error = 0;
            Assert.AreEqual(HotKeyResult.Ok, key.Recheck());
            Assert.IsTrue(key.IsRegistered);
            Assert.AreEqual(2, api.Registers);
        }
    }

    [TestMethod]
    public void AnotherRegisterErrorIsInvalid()
    {
        var (key, _, api) = Make(English);
        using (key)
        {
            api.Error = 87;
            Assert.AreEqual(HotKeyResult.Invalid, key.Apply("Ctrl+Alt+L"));
            Assert.IsFalse(key.IsRegistered);
        }
    }

    [TestMethod]
    public void ChangingTheShortcutLetsTheOldOneGo()
    {
        var (key, _, api) = Make(English);
        using (key)
        {
            key.Apply("Ctrl+Alt+L");
            Assert.AreEqual(HotKeyResult.Ok, key.Apply("Ctrl+Alt+K"));
            Assert.AreEqual(1, api.Unregisters);
            Assert.HasCount(1, api.Registered);
            Assert.AreEqual((uint)'K', api.Registered[0].Key);
            Assert.AreEqual("Ctrl+Alt+K", key.Shortcut);
        }
    }

    [TestMethod]
    public void EmptyIsOffAndRegistersNothing()
    {
        var (key, _, api) = Make(English);
        using (key)
        {
            key.Apply("Ctrl+Alt+L");
            Assert.AreEqual(HotKeyResult.Ok, key.Apply(""));
            Assert.IsFalse(key.IsRegistered);
            Assert.AreEqual(1, api.Unregisters);
            Assert.AreEqual(HotKeyResult.Ok, key.Apply(null));
            Assert.AreEqual(1, api.Registers);
            Assert.AreEqual("", key.Shortcut);
        }
    }

    [TestMethod]
    [DataRow("L")]
    [DataRow("Win+L")]
    [DataRow("Ctrl+Alt+Space")]
    [DataRow("nonsense")]
    public void BadTextIsInvalidAndRegistersNothing(string text)
    {
        var (key, _, api) = Make(English);
        using (key)
        {
            key.Apply("Ctrl+Alt+L");
            Assert.AreEqual(HotKeyResult.Invalid, key.Apply(text));
            Assert.IsFalse(key.IsRegistered);
            Assert.AreEqual(1, api.Unregisters);
            Assert.AreEqual(1, api.Registers);
        }
    }

    [TestMethod]
    public void NeverThrows()
    {
        var (key, layouts, api) = Make(English);
        using (key)
        {
            api.Throw = true;
            Assert.AreEqual(HotKeyResult.Invalid, key.Apply("Ctrl+Alt+L"));
            Assert.IsFalse(key.IsRegistered);
        }

        var throwing = new ThrowingLayouts();
        using var second = new HotKey(throwing, new FakeApi());
        Assert.AreEqual(HotKeyResult.Ok, second.Apply("Ctrl+Alt+L"));
        key.Dispose();
        Assert.AreEqual(HotKeyResult.Invalid, key.Apply("Ctrl+Alt+L"), "after Dispose it registers nothing");
    }

    sealed class ThrowingLayouts : IKeyboardLayouts
    {
        public IReadOnlyList<IntPtr> Installed() => new[] { English };

        public string Typed(IntPtr layout, Shortcut s) => throw new InvalidOperationException("no layout");
    }

    [TestMethod]
    public void CheckTellsWithoutRegistering()
    {
        var layouts = new FakeLayouts();
        layouts.List.AddRange(new[] { English, Polish });
        Assert.AreEqual(HotKeyResult.TypesALetter, HotKey.Check("Ctrl+Alt+L", layouts, out string? typing));
        Assert.AreEqual("04150415", typing);
        Assert.AreEqual(HotKeyResult.Ok, HotKey.Check("Ctrl+Alt+K", layouts, out _));
        Assert.AreEqual(HotKeyResult.Invalid, HotKey.Check("K", layouts, out _));
        Assert.AreEqual(HotKeyResult.Invalid, HotKey.Check("", layouts, out _));
        // The test key types nothing on any layout installed here.
        Assert.AreEqual(HotKeyResult.Ok, HotKey.Check(TestKey));
    }

    [TestMethod]
    public void PressedIsRaisedForItsOwnMessageOnly()
    {
        var (key, _, _) = Make(English);
        using (key)
        {
            int pressed = 0;
            key.Pressed += () => pressed++;
            key.Apply("Ctrl+Alt+L");
            IntPtr window = key.WindowHandle;
            Assert.AreNotEqual(IntPtr.Zero, window);

            SendMessage(window, HotKey.WmHotKey, new IntPtr(0x5E01), IntPtr.Zero);
            Assert.AreEqual(1, pressed);
            SendMessage(window, HotKey.WmHotKey, new IntPtr(7), IntPtr.Zero);
            Assert.AreEqual(1, pressed, "another hotkey id is not ours");

            // Off: a late message does nothing.
            key.Apply("");
            SendMessage(window, HotKey.WmHotKey, new IntPtr(0x5E01), IntPtr.Zero);
            Assert.AreEqual(1, pressed);
        }
    }

    [TestMethod]
    public void AFailingPressedHandlerIsCaught()
    {
        var (key, _, _) = Make(English);
        using (key)
        {
            key.Pressed += () => throw new InvalidOperationException("handler");
            key.Apply("Ctrl+Alt+L");
            SendMessage(key.WindowHandle, HotKey.WmHotKey, new IntPtr(0x5E01), IntPtr.Zero);
        }
    }

    // The real Windows calls, which the plain constructor never uses while the tests block real
    // effects. Only the test key below is ever registered through them.
    static HotKey Real() => new(KeyboardLayouts.Instance, HotKeyApi.Instance);

    // The plain constructor refuses every key under SessionLock.Blocked, even the lock shortcut.
    [TestMethod]
    public void ThePlainConstructorRegistersNothingWhileBlocked()
    {
        Assert.IsTrue(SessionLock.Blocked);
        using var key = new HotKey();
        Assert.AreNotEqual(HotKeyResult.Ok, key.Apply("Ctrl+Alt+L"));
        Assert.IsFalse(key.IsRegistered);
        Assert.AreNotEqual(HotKeyResult.Ok, key.Apply(TestKey));
        Assert.IsFalse(key.IsRegistered);
        Assert.AreEqual(HotKeyResult.Ok, key.Apply(""));
    }

    // The only real registration: Ctrl+Alt+Shift+F24, then unregistered.
    [TestMethod]
    [DoNotParallelize]
    public void RegistersAndUnregistersTheTestKeyForReal()
    {
        using var first = Real();
        HotKeyResult result = first.Apply(TestKey);
        if (result == HotKeyResult.InUse) Assert.Inconclusive(TestKey + " is taken by another app on this PC");
        Assert.AreEqual(HotKeyResult.Ok, result);
        Assert.IsTrue(first.IsRegistered);

        // While it is held, a second window can't have it.
        using (var second = Real())
        {
            Assert.AreEqual(HotKeyResult.InUse, second.Apply(TestKey));
            Assert.IsFalse(second.IsRegistered);
        }

        // Unregistered: now the second one gets it, and lets it go when it is disposed.
        Assert.AreEqual(HotKeyResult.Ok, first.Apply(""));
        using (var third = Real())
        {
            Assert.AreEqual(HotKeyResult.Ok, third.Apply(TestKey));
            Assert.IsTrue(third.IsRegistered);
        }
        Assert.AreEqual(HotKeyResult.Ok, first.Apply(TestKey));
        first.Dispose();
        using var last = Real();
        Assert.AreEqual(HotKeyResult.Ok, last.Apply(TestKey), "Dispose unregistered it");
        Assert.AreEqual(HotKeyResult.Ok, last.Apply(""));
    }

    [TestMethod]
    public void TheRealLayoutListHasTheLayoutInUse()
    {
        IReadOnlyList<IntPtr> installed = KeyboardLayouts.Instance.Installed();
        Assert.IsNotEmpty(installed);
        Assert.AreEqual(installed.Count, installed.Distinct().Count());
    }

    [TestMethod]
    public void RealLayoutsTypeAPlainKeyAndNothingForF24()
    {
        foreach (IntPtr layout in KeyboardLayouts.Instance.Installed())
        {
            // Proves the call works: a plain A types something on every layout.
            Assert.AreNotEqual("", KeyboardLayouts.Typed(layout, (uint)Keys.A, false, false, false), "layout " + layout.ToString("X"));
            // Ctrl with a letter is a control character, which is not typing.
            Assert.AreEqual("", KeyboardLayouts.Typed(layout, (uint)Keys.L, true, false, false));
            Assert.AreEqual("", KeyboardLayouts.Instance.Typed(layout, Shortcut.Parse(TestKey)!.Value));
        }
    }

    [TestMethod]
    public void RealUsEnglishTypesNothingWithCtrlAltL()
    {
        IntPtr us = KeyboardLayouts.Instance.Installed().FirstOrDefault(l => ((long)l & 0xFFFFFFFF) == 0x04090409);
        if (us == IntPtr.Zero) Assert.Inconclusive("US English is not installed here");
        Assert.AreEqual("", KeyboardLayouts.Instance.Typed(us, Shortcut.Default));
        Assert.AreEqual("l", KeyboardLayouts.Typed(us, (uint)Keys.L, false, false, false));
    }

    [TestMethod]
    public void AltWithoutCtrlNeverTypesOnAnyRealLayout()
    {
        // Alt+key is a menu key in apps, never a typed character; only Ctrl+Alt is AltGr.
        Keys[] keys = { Keys.A, Keys.K, Keys.L, Keys.Q, Keys.Z, Keys.D0, Keys.D9 };
        foreach (IntPtr layout in KeyboardLayouts.Instance.Installed())
        {
            foreach (Keys k in keys)
            {
                Assert.AreEqual("", KeyboardLayouts.Typed(layout, (uint)k, false, true, false), "Alt+" + k + " on " + layout.ToString("X"));
                Assert.AreEqual("", KeyboardLayouts.Typed(layout, (uint)k, false, true, true), "Alt+Shift+" + k + " on " + layout.ToString("X"));
            }
        }
    }

    [TestMethod]
    public void RealLayoutsAcceptAltAndAltShiftShortcuts()
    {
        foreach (string s in new[] { "Alt+K", "Alt+9", "Alt+Shift+K", "Alt+Shift+L", "Alt+F7" })
            Assert.AreEqual(HotKeyResult.Ok, HotKey.Check(s), s);
    }

    [TestMethod]
    public void EveryAllowedAltAndAltShiftShortcutIsOkOnTheRealLayouts()
    {
        int count = 0;
        foreach (ShortcutModifiers mods in new[] { ShortcutModifiers.Alt, ShortcutModifiers.Alt | ShortcutModifiers.Shift })
        {
            foreach (Keys key in Enum.GetValues<Keys>().Distinct())
            {
                if (!Shortcut.IsAllowed(mods, key)) continue;
                count++;
                string text = new Shortcut(mods, key).ToString();
                Assert.AreEqual(HotKeyResult.Ok, HotKey.Check(text), text);
            }
        }
        Assert.IsGreaterThan(100, count);
    }

    [TestMethod]
    public void RealUsEnglishAltKIsOk()
    {
        IntPtr us = KeyboardLayouts.Instance.Installed().FirstOrDefault(l => ((long)l & 0xFFFFFFFF) == 0x04090409);
        if (us == IntPtr.Zero) Assert.Inconclusive("US English is not installed here");
        Assert.AreEqual("", KeyboardLayouts.Typed(us, (uint)Keys.K, false, true, false));
        Assert.AreEqual(HotKeyResult.Ok, HotKey.Check("Alt+K"));
    }

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
