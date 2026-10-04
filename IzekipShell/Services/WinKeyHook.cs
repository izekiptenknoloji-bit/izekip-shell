using System.Runtime.InteropServices;
using IzekipShell.Interop;

namespace IzekipShell.Services;

// Windows tusu tek basina birakilinca Windows'un Baslat'i yerine bizimki acilir.
// Win+D bizim "masaustunu goster"e, Win+S bizim aramaya gider; diger kisayollar (Win+E, Win+L, ...) aynen gecer.
// Win birakilmadan hemen once anlamsiz bir tus (0xE8) gonderilir: Windows bunu kisayol sayar ve
// kendi menusunu acmaz. Geri cagrimlar kanca icinden cagrilir, hizli donmeli.
public static class WinKeyHook
{
    const int WH_KEYBOARD_LL = 13;
    const uint VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_D = 0x44, VK_S = 0x53;
    const byte MaskKey = 0xE8;
    const uint LLKHF_INJECTED = 0x10;

    static Native.HookProc? _proc;
    static IntPtr _hook;
    static Action? _onStart, _onDesktop, _onSearch;
    static bool _winDown, _anyOther, _passedOther;
    static uint _held; // yutulan kisayol tusu (D ya da S)

    public static void Install(Action onStart, Action onDesktop, Action onSearch)
    {
        _onStart = onStart;
        _onDesktop = onDesktop;
        _onSearch = onSearch;
        _proc = Proc;
        _hook = Native.SetWindowsHookEx(WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
    }

    public static void Uninstall()
    {
        if (_hook != 0) Native.UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    static IntPtr Proc(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(l);
            bool down = w == 0x100 || w == 0x104;
            bool up = w == 0x101 || w == 0x105;
            if ((k.flags & LLKHF_INJECTED) == 0)
            {
                if (k.vkCode is VK_LWIN or VK_RWIN)
                {
                    if (down && !_winDown)
                    {
                        _winDown = true;
                        _anyOther = _passedOther = false;
                    }
                    else if (up && _winDown)
                    {
                        _winDown = false;
                        if (!_passedOther)
                        {
                            Native.keybd_event(MaskKey, 0, 0, 0);
                            Native.keybd_event(MaskKey, 0, 2 /* KEYEVENTF_KEYUP */, 0);
                        }
                        if (!_anyOther) _onStart?.Invoke();
                    }
                }
                else if ((k.vkCode is VK_D or VK_S && _winDown) || k.vkCode == _held)
                {
                    if (down && _held == 0)
                    {
                        _held = k.vkCode;
                        _anyOther = true;
                        (k.vkCode == VK_D ? _onDesktop : _onSearch)?.Invoke();
                    }
                    else if (up) _held = 0;
                    return 1; // Win+D / Win+S yutulur
                }
                else if (_winDown && down)
                {
                    _anyOther = true;
                    _passedOther = true;
                }
            }
        }
        return Native.CallNextHookEx(_hook, code, w, l);
    }
}
