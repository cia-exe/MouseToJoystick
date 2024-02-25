﻿using Gma.System.MouseKeyHook;
using vJoyInterfaceWrap;

namespace MouseToJoystick2
{
    class MouseToJoystickHandler : IDisposable
    {
        readonly int invertX;
        readonly int invertY;
        readonly bool autoCenter;
        readonly bool autoSize;
        readonly int manualWidth;
        readonly int manualHeight;

        // MouseKeyHook stuff
        private IKeyboardMouseEvents? mouseEventHooker = null;

        private int lastX;
        private int lastY;

        // vJoy stuff
        private vJoy? joystick = null;
        private readonly uint id;

        private readonly long AXIS_MAX;
        private readonly long AXIS_MIN;
        private readonly long AXIS_MID;

        private const uint VJOY_BTN_1 = 1;
        private const uint VJOY_BTN_2 = 2;
        private const uint VJOY_BTN_3 = 3;

        public MouseToJoystickHandler(uint vjoyDevId, bool invertX, bool invertY, bool autoCenter, bool autoSize, int manualWidth, int manualHeight)
        {
            this.id = vjoyDevId;
            this.invertX = invertX ? -1 : 1;
            this.invertY = invertY ? -1 : 1;
            this.autoCenter = autoCenter;
            this.autoSize = autoSize;
            this.manualWidth = manualWidth;
            this.manualHeight = manualHeight;

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


            if (!this.joystick.GetVJDAxisMax(this.id, HID_USAGES.HID_USAGE_X, ref this.AXIS_MAX))
            {
                throw new Exception("Failed to get vJoy axis max");
            }

            if (!this.joystick.GetVJDAxisMin(this.id, HID_USAGES.HID_USAGE_X, ref this.AXIS_MIN))
            {
                throw new Exception("Failed to get vJoy axis min");
            }
            this.AXIS_MID = AXIS_MAX - (AXIS_MAX - AXIS_MIN) / 2;

            // Register for mouse events
            mouseEventHooker = Hook.GlobalEvents();
            mouseEventHooker.MouseMove += HandleMouseMoveFirst;
            mouseEventHooker.MouseDown += HandleMouseDown;
            mouseEventHooker.MouseUp += HandleMouseUp;
        }

        private void HandleMouseButton(MouseEventArgs e, bool down)
        {
            uint btnId = e.Button switch
            {
                MouseButtons.Left => VJOY_BTN_1,
                MouseButtons.Right => VJOY_BTN_2,
                MouseButtons.Middle => VJOY_BTN_3,
                MouseButtons.XButton1 => 4, // backward
                MouseButtons.XButton2 => 5, // foreward
                _ => 0,
            };

            if (btnId > 0) joystick?.SetBtn(down, id, btnId);
        }

        private void HandleMouseDown(object? sender, MouseEventArgs e) => HandleMouseButton(e, true);

        private void HandleMouseUp(object? sender, MouseEventArgs e) => HandleMouseButton(e, false);

        private void HandleMouseMoveFirst(object? sender, MouseEventArgs e)
        {
            this.lastX = e.X;
            this.lastY = e.Y;

            if (mouseEventHooker == null) return;
            mouseEventHooker.MouseMove -= HandleMouseMoveFirst;
            mouseEventHooker.MouseMove += HandleMouseMove;
        }

        private void HandleMouseMove(object? sender, MouseEventArgs e)
        {
            if (joystick == null) return;
            var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 0, 0);

            var minX = bounds.Left;
            var maxX = this.autoSize ? bounds.Right : (bounds.Left + this.manualWidth);

            var minY = bounds.Top;
            var maxY = this.autoSize ? bounds.Bottom : (bounds.Top + this.manualHeight);

            int deltaX = e.X - this.lastX;
            int deltaY = e.Y - this.lastY;
            this.lastX = this.autoCenter ? Clamp<int>(minX, e.X, maxX) : (minX + (maxX - minX) / 2);
            this.lastY = this.autoCenter ? Clamp<int>(minY, e.Y, maxY) : (minY + (maxY - minY) / 2);

            int xOut, yOut;
            if (this.autoCenter)
            {
                xOut = Clamp<int>(Convert.ToInt32(AXIS_MIN), (int)Math.Round(AXIS_MID + invertX * (deltaX * (deltaX * -1.0 / 1.1 + 500))), Convert.ToInt32(AXIS_MAX));
                yOut = Clamp<int>(Convert.ToInt32(AXIS_MIN), (int)Math.Round(AXIS_MID + invertY * (deltaY * (deltaY * -1.0 / 1.1 + 500))), Convert.ToInt32(AXIS_MAX));
            }
            else
            {
                int maxDeltaX = this.autoSize ? bounds.Width : this.manualWidth;
                int maxDeltaY = this.autoSize ? bounds.Height : this.manualHeight;
                long outputPerDeltaX = (AXIS_MAX - AXIS_MIN) / maxDeltaX;
                long outputPerDeltaY = (AXIS_MAX - AXIS_MIN) / maxDeltaY;
                xOut = Clamp<int>(Convert.ToInt32(AXIS_MIN), (int)(AXIS_MID + invertX * deltaX * outputPerDeltaX), Convert.ToInt32(AXIS_MAX));
                yOut = Clamp<int>(Convert.ToInt32(AXIS_MIN), (int)(AXIS_MID + invertY * deltaY * outputPerDeltaY), Convert.ToInt32(AXIS_MAX));
            }

            //Console.WriteLine(String.Format("{0}, {1} -> {2}, {3}", lastX, lastY, xOut, yOut));

            joystick.SetAxis(xOut, this.id, HID_USAGES.HID_USAGE_X);
            joystick.SetAxis(yOut, this.id, HID_USAGES.HID_USAGE_Y);
        }

        private static T Clamp<T>(T min, T val, T max) where T : IComparable<T>
        {
            if (val.CompareTo(min) < 0) return min;
            else if (val.CompareTo(max) > 0) return max;
            else return val;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
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
    }
}
