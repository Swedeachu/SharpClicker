using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AutoClicker {
  class Program {

    // Low-level mouse hook constants
    private const int WH_MOUSE_LL = 14;
    private const int WH_KEYBOARD_LL = 13;

    private const uint LLMHF_INJECTED = 1;
    private const uint LLMHF_LOWER_IL_INJECTED = 2;

    private static IntPtr _mouseHookID = IntPtr.Zero;
    private static IntPtr _keyboardHookID = IntPtr.Zero;

    private static volatile bool isClickerEnabled = false;
    private static volatile bool isLeftMouseDown = false;
    private static volatile bool exitRequested = false;
    private static int minCps;
    private static int maxCps;

    private static Thread clickerThread;
    private static Random random = new Random();

    [DllImport("kernel32.dll")]
    static extern bool AllocConsole();

    static void Main(string[] args) {
      AllocConsole();

      Console.Write("Enter minimum CPS: ");
      while (!int.TryParse(Console.ReadLine(), out minCps) || minCps <= 0) {
        Console.WriteLine("Please enter a valid positive integer for minimum CPS:");
      }

      Console.Write("Enter maximum CPS: ");
      while (!int.TryParse(Console.ReadLine(), out maxCps) || maxCps < minCps) {
        Console.WriteLine($"Please enter a valid integer greater than or equal to {minCps} for maximum CPS:");
      }

      Console.WriteLine("Press 'F' to toggle the autoclicker on/off. It is currently Off.");
      Console.WriteLine("Hold down the left mouse button to start clicking.");
      Console.WriteLine("Press 'Enter' to exit the program. (must be focused on the console window)");

      // Set up hooks
      _keyboardHookID = SetKeyboardHook(KeyboardProc);
      _mouseHookID = SetMouseHook(MouseProc);

      // Start the clicker thread
      clickerThread = new Thread(ClickerLoop);
      clickerThread.IsBackground = true;
      clickerThread.Start();

      // Start a separate thread to monitor for the Enter key
      Thread exitThread = new Thread(ExitMonitor);
      exitThread.Start();

      Application.Run();

      // Clean up
      UnhookWindowsHookEx(_mouseHookID);
      UnhookWindowsHookEx(_keyboardHookID);
    }

    private static void ExitMonitor() {
      while (Console.ReadKey(true).Key != ConsoleKey.Enter) {
        Thread.Sleep(10);
      }
      exitRequested = true;
      Application.Exit();
    }

    private static IntPtr SetMouseHook(LowLevelMouseProc proc) {
      using (Process curProcess = Process.GetCurrentProcess())
      using (ProcessModule curModule = curProcess.MainModule) {
        return SetWindowsHookEx(WH_MOUSE_LL, proc,
            GetModuleHandle(curModule.ModuleName), 0);
      }
    }

    private static IntPtr SetKeyboardHook(LowLevelKeyboardProc proc) {
      using (Process curProcess = Process.GetCurrentProcess())
      using (ProcessModule curModule = curProcess.MainModule) {
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
            GetModuleHandle(curModule.ModuleName), 0);
      }
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private static LowLevelMouseProc _mouseProc = MouseProc;

    private static IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam) {
      if (exitRequested)
        return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);

      if (nCode >= 0) {
        MSLLHOOKSTRUCT mouseStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        if (mouseStruct.flags == 0) { // Ensure it's not injected
          int wmMouse = wParam.ToInt32();

          if (wmMouse == WM_LBUTTONDOWN) {
            isLeftMouseDown = true;
          } else if (wmMouse == WM_LBUTTONUP) {
            isLeftMouseDown = false;
          }
        }
      }

      return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private static LowLevelKeyboardProc _keyboardProc = KeyboardProc;

    private static IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam) {
      if (exitRequested)
        return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);

      if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN) {
        KBDLLHOOKSTRUCT kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if (kbStruct.vkCode == (uint)Keys.F) {
          isClickerEnabled = !isClickerEnabled;
          Console.WriteLine("Autoclicker is now " + (isClickerEnabled ? "ON" : "OFF"));
        }
      }

      return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
    }

    private static void ClickerLoop() {
      Stopwatch stopwatch = new Stopwatch();
      stopwatch.Start();

      while (!exitRequested) {
        if (isClickerEnabled && isLeftMouseDown) {
          // Determine CPS and calculate delay
          int cps = random.Next(minCps, maxCps + 1);
          int delay = 1000 / cps;
          int randomOffset = random.Next(-30, 30); // ±30 ms randomization

          // Simulate the mouse click
          SimulateMouseClick();

          // Wait for the next click, adjusted by random offset
          int totalDelay = delay + randomOffset;

          // Ensure the delay is within reasonable bounds
          if (totalDelay < 10) totalDelay = 10;
          if (totalDelay > 1000) totalDelay = 1000;

          Thread.Sleep(totalDelay);
        } else {
          // Sleep briefly to reduce CPU usage when not clicking
          Thread.Sleep(10);
        }
      }
    }

    private static void SimulateMouseClick() {
      INPUT[] inputs = new INPUT[2];

      // Mouse down
      inputs[0].type = InputType.INPUT_MOUSE;
      inputs[0].mi.dwFlags = MouseEventF.LEFTDOWN;

      // Mouse up
      inputs[1].type = InputType.INPUT_MOUSE;
      inputs[1].mi.dwFlags = MouseEventF.LEFTUP;

      // Send the inputs
      uint result = SendInput((uint)inputs.Length, inputs, INPUT.Size);
      if (result == 0) {
        int errorCode = Marshal.GetLastWin32Error();
        Console.WriteLine("SendInput failed: " + errorCode);
      }
    }

    // Constants and structs for hooks and input simulation below

    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_KEYDOWN = 0x0100;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT {
      public POINT pt;
      public uint mouseData;
      public uint flags;
      public uint time;
      public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT {
      public uint vkCode;
      public uint scanCode;
      public uint flags;
      public uint time;
      public IntPtr dwExtraInfo;
    }

    #region Native Methods

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook,
        LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook,
        LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
        IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    #endregion

    #region Input Simulation Structures

    private struct INPUT {
      public InputType type;
      public MOUSEINPUT mi;

      public static int Size {
        get { return Marshal.SizeOf(typeof(INPUT)); }
      }
    }

    private struct MOUSEINPUT {
      public int dx;
      public int dy;
      public uint mouseData;
      public MouseEventF dwFlags;
      public uint time;
      public IntPtr dwExtraInfo;
    }

    private enum InputType : uint {
      INPUT_MOUSE = 0
    }

    [Flags]
    private enum MouseEventF : uint {
      LEFTDOWN = 0x0002,
      LEFTUP = 0x0004
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT {
      public int x;
      public int y;
    }

    #endregion
  }

}
