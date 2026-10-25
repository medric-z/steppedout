using System.Runtime.InteropServices;

namespace SteppedOut;

// One reading of a game controller, as XInput gives it.
readonly record struct PadState(
    uint Packet,
    ushort Buttons,
    byte LeftTrigger,
    byte RightTrigger,
    short LeftX,
    short LeftY,
    short RightX,
    short RightY);

// The game controllers in slots 0 to 3, behind a seam so tests can use made-up readings.
interface IPads
{
    // False when no controller is in this slot, or the controllers can't be read at all.
    bool TryRead(int slot, out PadState state);
}

// Xbox-compatible controllers through XInput. Windows 8 and later have xinput1_4.dll; older
// systems and some trimmed ones only have xinput9_1_0.dll. If neither loads, there are no
// controllers. Reading only asks for the state: it never sends anything to a controller.
sealed class XInputPads : IPads
{
    public static XInputPads Instance { get; } = new();

    const int ErrorSuccess = 0;

    // 0: not tried yet, 1: xinput1_4, 2: xinput9_1_0, -1: neither.
    int library;

    public bool Available => library >= 0;

    public bool TryRead(int slot, out PadState state)
    {
        state = default;
        if ((uint)slot > 3) return false;
        int result;
        XINPUT_STATE raw;
        switch (library)
        {
            case 0:
            case 1:
                try
                {
                    result = GetState14((uint)slot, out raw);
                    library = 1;
                    break;
                }
                catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
                {
                    library = 2;
                    goto case 2;
                }
            case 2:
                try
                {
                    result = GetState910((uint)slot, out raw);
                    break;
                }
                catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
                {
                    library = -1;
                    Log.Write("game controllers can't be read: XInput is missing (" + e.Message + ")");
                    return false;
                }
            default:
                return false;
        }
        if (result != ErrorSuccess) return false;
        XINPUT_GAMEPAD p = raw.Gamepad;
        state = new PadState(raw.dwPacketNumber, p.wButtons, p.bLeftTrigger, p.bRightTrigger, p.sThumbLX, p.sThumbLY, p.sThumbRX, p.sThumbRY);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    static extern int GetState14(uint userIndex, out XINPUT_STATE state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    static extern int GetState910(uint userIndex, out XINPUT_STATE state);
}
