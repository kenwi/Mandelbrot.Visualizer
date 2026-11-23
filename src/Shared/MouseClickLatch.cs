using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ZeroElectric.Vinculum;

namespace Mandelbrot.Visualizer.Shared;

/// <summary>
/// Chains onto GLFW's native mouse button callback (bundled inside raylib) to latch
/// per-frame press and release transitions. Without this latch, sub-frame clicks
/// (such as laptop touchpad tap-to-click or fast clicks completed during a single
/// frame/VSync wait) overwrite Raylib's currentButtonState from 0 -> 1 -> 0 inside
/// glfwPollEvents and are silently dropped by IsMouseButtonPressed / Released.
/// </summary>
public static unsafe class MouseClickLatch
{
    public const int GlfwRelease = 0;
    public const int GlfwPress = 1;

    const int ButtonCount = 8;

    static readonly bool[] s_pendingPressed = new bool[ButtonCount];
    static readonly bool[] s_pendingReleased = new bool[ButtonCount];
    static readonly bool[] s_framePressed = new bool[ButtonCount];
    static readonly bool[] s_frameReleased = new bool[ButtonCount];

    static IntPtr s_libraryHandle;
    static IntPtr s_window;
    static delegate* unmanaged[Cdecl]<IntPtr, int, int, int, void> s_previousCallback;
    static delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, int, int, int, void>, delegate* unmanaged[Cdecl]<IntPtr, int, int, int, void>> s_setMouseButtonCallback;
    static bool s_installed;

    public static bool IsInstalled => s_installed;

    public static void Install()
    {
        if (s_installed) return;

        ClearAll();

        if (!TryResolveGlfw(out IntPtr handle, out var getCurrentContext, out var setMouseButtonCallback))
            return;

        IntPtr window = getCurrentContext();
        if (window == IntPtr.Zero)
        {
            NativeLibrary.Free(handle);
            return;
        }

        s_libraryHandle = handle;
        s_window = window;
        s_setMouseButtonCallback = setMouseButtonCallback;
        s_previousCallback = setMouseButtonCallback(window, &OnMouseButton);
        s_installed = true;
    }

    public static void Uninstall()
    {
        if (!s_installed) return;

        if (s_window != IntPtr.Zero && s_setMouseButtonCallback != null)
            s_setMouseButtonCallback(s_window, s_previousCallback);

        if (s_libraryHandle != IntPtr.Zero)
            NativeLibrary.Free(s_libraryHandle);

        s_libraryHandle = IntPtr.Zero;
        s_window = IntPtr.Zero;
        s_previousCallback = null;
        s_setMouseButtonCallback = null;
        s_installed = false;
        ClearAll();
    }

    public static void BeginFrame()
    {
        for (int i = 0; i < ButtonCount; i++)
        {
            s_framePressed[i] = s_pendingPressed[i];
            s_frameReleased[i] = s_pendingReleased[i];
            s_pendingPressed[i] = false;
            s_pendingReleased[i] = false;
        }
    }

    public static bool IsPressed(MouseButton button)
        => IsLatchedPressed(button) || Raylib.IsMouseButtonPressed(button);

    public static bool IsReleased(MouseButton button)
        => IsLatchedReleased(button) || Raylib.IsMouseButtonReleased(button);

    public static bool IsDown(MouseButton button)
        => Raylib.IsMouseButtonDown(button);

    internal static bool IsLatchedPressed(MouseButton button)
    {
        int index = (int)button;
        return (uint)index < ButtonCount && s_framePressed[index];
    }

    internal static bool IsLatchedReleased(MouseButton button)
    {
        int index = (int)button;
        return (uint)index < ButtonCount && s_frameReleased[index];
    }

    internal static void RecordButtonEvent(int button, int action)
    {
        if ((uint)button >= ButtonCount)
            return;

        if (action == GlfwPress)
            s_pendingPressed[button] = true;
        else if (action == GlfwRelease)
            s_pendingReleased[button] = true;
    }

    internal static bool CanResolveGlfwExports()
    {
        if (!TryResolveGlfw(out IntPtr handle, out _, out _))
            return false;

        NativeLibrary.Free(handle);
        return true;
    }

    internal static void ClearAll()
    {
        Array.Clear(s_pendingPressed);
        Array.Clear(s_pendingReleased);
        Array.Clear(s_framePressed);
        Array.Clear(s_frameReleased);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnMouseButton(IntPtr window, int button, int action, int mods)
    {
        RecordButtonEvent(button, action);

        if (s_previousCallback != null)
            s_previousCallback(window, button, action, mods);
    }

    static bool TryResolveGlfw(
        out IntPtr handle,
        out delegate* unmanaged[Cdecl]<IntPtr> getCurrentContext,
        out delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, int, int, int, void>, delegate* unmanaged[Cdecl]<IntPtr, int, int, int, void>> setMouseButtonCallback)
    {
        handle = IntPtr.Zero;
        getCurrentContext = null;
        setMouseButtonCallback = null;

        if (!NativeLibrary.TryLoad("raylib", typeof(Raylib).Assembly, null, out IntPtr lib))
            return false;

        if (!NativeLibrary.TryGetExport(lib, "glfwGetCurrentContext", out IntPtr getCtxPtr)
            || !NativeLibrary.TryGetExport(lib, "glfwSetMouseButtonCallback", out IntPtr setCbPtr))
        {
            NativeLibrary.Free(lib);
            return false;
        }

        handle = lib;
        getCurrentContext = (delegate* unmanaged[Cdecl]<IntPtr>)getCtxPtr;
        setMouseButtonCallback = (delegate* unmanaged[Cdecl]<IntPtr, delegate* unmanaged[Cdecl]<IntPtr, int, int, int, void>, delegate* unmanaged[Cdecl]<IntPtr, int, int, int, void>>)setCbPtr;
        return true;
    }
}
