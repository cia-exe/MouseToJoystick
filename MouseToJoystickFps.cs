using Gma.System.MouseKeyHook;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using vJoyInterfaceWrap;

using Cia.Exe;
using static Cia.Exe.LooperHandler;
using static Cia.Exe.Util;

namespace MouseToJoystick2
{
    class MouseToJoystickFps : IDisposable
    {
        private  readonly LooperHandler handler = new();


        // MouseKeyHook stuff
        private IKeyboardMouseEvents? mouseEventHooker = null;


        // vJoy stuff
        private vJoy? joystick = null;
        private readonly uint id;

        private readonly long AXIS_MAX;
        private readonly long AXIS_MIN;
        private readonly long AXIS_MID;

        private const uint VJOY_BTN_1 = 1;
        private const uint VJOY_BTN_2 = 2;
        private const uint VJOY_BTN_3 = 3;

        public MouseToJoystickFps(uint vjoyDevId)
        {
            this.id = vjoyDevId;


            joystick = new vJoy();

            // Make sure driver is enabled
            if (!joystick.vJoyEnabled())
            {
                throw new InvalidOperationException("vJoy driver not enabled: Failed Getting vJoy attributes");
            }

            // Make sure we can get the joystick
            VjdStat status = joystick.GetVJDStatus(id);
            switch (status)
            {
                case VjdStat.VJD_STAT_OWN:
                case VjdStat.VJD_STAT_FREE:
                    break;

                case VjdStat.VJD_STAT_BUSY:
                    throw new InvalidOperationException("vJoy device is already owned by another feeder");

                case VjdStat.VJD_STAT_MISS:
                    throw new InvalidOperationException("vJoy device is not installed or is disabled");

                default:
                    throw new Exception("vJoy device general error");
            };

            if (!this.joystick.AcquireVJD(this.id))
            {
                throw new Exception("Failed to acquire vJoy device");
            }

            if (!this.joystick.ResetVJD(this.id))
            {
                throw new Exception("Failed to reset vJoy device");
            }


            if (!this.joystick.GetVJDAxisMax(this.id, HID_USAGES.HID_USAGE_X, ref this.AXIS_MAX)) //0x7fff
            {
                throw new Exception("Failed to get vJoy axis max");
            }

            if (!this.joystick.GetVJDAxisMin(this.id, HID_USAGES.HID_USAGE_X, ref this.AXIS_MIN)) //0
            {
                throw new Exception("Failed to get vJoy axis min");
            }
            this.AXIS_MID = AXIS_MAX - (AXIS_MAX - AXIS_MIN) / 2; // 0x4000

            //init
            int m = (int)AXIS_MID;
            int z = (int)AXIS_MIN;
            _ = joystick.SetAxis(m, id, HID_USAGES.HID_USAGE_X);
            _ = joystick.SetAxis(m, id, HID_USAGES.HID_USAGE_Y);
            _ = joystick.SetAxis(z, id, HID_USAGES.HID_USAGE_Z);
            _ = joystick.SetAxis(m, id, HID_USAGES.HID_USAGE_RX);
            _ = joystick.SetAxis(m, id, HID_USAGES.HID_USAGE_RY);
            _ = joystick.SetAxis(z, id, HID_USAGES.HID_USAGE_RZ);

            // Register for mouse events
            mouseEventHooker = Hook.GlobalEvents();

            mouseEventHooker.KeyDown += HandleKeyDown;
            //mouseEventHooker.KeyPress += HandleKeyPress;

            //mouseEventHooker.MouseMoveExt += HandleMouseMoveFirst;
            //mouseEventHooker.MouseMoveExt += HandleMouseMove;
            mouseEventHooker.MouseDownExt += HandleMouseDown;
            mouseEventHooker.MouseUpExt += HandleMouseUp;
            mouseEventHooker.MouseWheelExt += HandleMouseWheel;
        }

        private void HandleMouseButton(MouseEventExtArgs e, bool down)
        {
            Debug.WriteLine($"{Tid()} MouseButton({down}): b={e.Button} c={e.Clicks} d={e.Delta} l={e.Location} t={e.Timestamp}");
            if (mouseDisabled) e.Handled = true;  // suppress the mouse button click
            if (joystick == null) return;

            uint btnId = e.Button switch
            {
                MouseButtons.Left => (uint)HID_USAGES.HID_USAGE_RZ, //VJOY_BTN_1,
                MouseButtons.Right => 1,
                MouseButtons.Middle => 6,
                MouseButtons.XButton1 => 3, // backward
                MouseButtons.XButton2 => 8, // foreward
                _ => 0,
            };

            if (btnId == (int)HID_USAGES.HID_USAGE_RZ) _ = joystick.SetAxis((int)(down ? AXIS_MAX : AXIS_MIN), id, (HID_USAGES)btnId);
            else if (btnId > 0) _ = joystick.SetBtn(down, id, btnId);
        }

        private void HandleMouseDown(object? sender, MouseEventExtArgs e) => HandleMouseButton(e, true);

        private void HandleMouseUp(object? sender, MouseEventExtArgs e) => HandleMouseButton(e, false);


        private void HandleMouseMove(object? sender, MouseEventExtArgs e)
        {
            Debug.WriteLine($"{Tid()} MouseMove: b={e.Button} c={e.Clicks} d={e.Delta} l={e.Location} t={e.Timestamp}");

            if (joystick == null) return;
            //Debug.WriteLine($"MouseMove: b={e.Button} c={e.Clicks} d={e.Delta} x={e.X} y={e.Y}");
            //Thread.Sleep(1000);

            if (mouseDisabled) e.Handled = true;
        }



        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            Debug.WriteLine($"{Tid()} Dispose({disposing}):{disposedValue}");

            if (!disposedValue)
            {
                if (disposing)
                {
                    if (this.mouseEventHooker != null)
                    {
                        this.mouseEventHooker.Dispose();
                        this.mouseEventHooker = null;
                    }

                    // dispose managed state (managed objects).
                    if (this.joystick != null)
                    {
                        this.joystick.RelinquishVJD(this.id);
                        this.joystick = null;
                    }

                    handler.Dispose();
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion



        //-------------------
        #region new

        private bool mouseDisabled = false;
        private void HandleKeyDown(object? sender, KeyEventArgs e)
        {
            // for all keys
            Debug.WriteLine($"{Tid()} HandleKeyDown: v({e.KeyValue}), c({e.KeyCode}), d[{e.KeyData}] m:[{e.Modifiers}], c={e.Control} a={e.Alt} s={e.Shift}");
            // v:123, d:F12, Control, Alt, c:F12, m:Control, Alt, c:True a:True s:False

            //if(e.Control && e.Alt && e.KeyCode == Keys.F12)
            if (e.KeyData == (Keys.Z | Keys.Control | Keys.Alt))
            {
                mouseDisabled = !mouseDisabled;
                Debug.WriteLine($"!!! HandleKeyDown:[ {e.KeyData} ] {mouseDisabled}");
            }

            handler.PostDelayed(() => { Debug.WriteLine($"{Tid()} Action 2 executed after 1000 milliseconds"); }, 1000);
        }

        private void HandleKeyPress(object? sender, KeyPressEventArgs e)
        {
            // case sensetive, no ctrl, esc, backspace... key
            Debug.WriteLine($"HandleKeyPress: \t{(int)e.KeyChar};");
        }


        int wheelCount = 0;
        private void HandleMouseWheel(object? sender, MouseEventExtArgs e)
        {
            static string func(int d) => d < 0 ? $"({d})" : $"{d}";

            if (mouseDisabled) e.Handled = true;
            Debug.WriteLine($"{Tid()} MouseWheel: b={e.Button} c={e.Clicks} d={func(e.Delta)} x={e.X} y={e.Y}");
            //Thread.Sleep(3000);
            Debug.WriteLine($"-MouseWheel:{wheelCount++}");


        }

        #endregion

    }
}
