using System;
using System.Drawing;
using System.Windows.Forms;

namespace ESPWorkbench
{
    internal sealed class QuickPanel : NativeWindow,IDisposable
    {
        private readonly AlphaSurface surface=new AlphaSurface();
        private readonly RenderOptions options;
        private readonly QuickHudState state;
        private QuickHudLayout layout;
        private QuickHudAction pressed,hover;
        private Rectangle viewport;
        private Point dragOffset;
        private bool dirty=true,paused;
        private long movements;
        private double hertz;
        private string aimState;
        private int? nearby;
        private DateTime nextStatus;

        internal event Action<QuickHudAction> ClickAction;
        internal event Action PositionChanged,AdjustmentChanged;
        internal bool IsInteracting { get { return pressed!=QuickHudAction.None; } }
        internal bool Visible { get { return Handle!=IntPtr.Zero&&Native.IsWindowVisible(Handle); } }
        internal Rectangle Bounds { get; private set; }

        internal QuickPanel(RenderOptions options,QuickHudState state)
        {
            this.options=options;
            this.state=state;
        }

        protected override void WndProc(ref Message message)
        {
            switch(message.Msg)
            {
                case 0x0021: // WM_MOUSEACTIVATE
                    message.Result=new IntPtr(3); // MA_NOACTIVATE
                    return;
                case 0x0020: // WM_SETCURSOR
                    Cursor.Current=hover==QuickHudAction.Move?Cursors.SizeAll:hover==QuickHudAction.None?Cursors.Default:Cursors.Hand;
                    message.Result=new IntPtr(1);
                    return;
                case 0x0201: // WM_LBUTTONDOWN
                    BeginInteraction(MousePoint(message.LParam));
                    return;
                case 0x0202: // WM_LBUTTONUP
                    ReleaseInteraction(MousePoint(message.LParam));
                    return;
            }
            base.WndProc(ref message);
        }

        private static Point MousePoint(IntPtr parameter)
        {
            long value=parameter.ToInt64();
            return new Point((short)(value&0xffff),(short)((value>>16)&0xffff));
        }

        internal void RefreshContent() { dirty=true; }

        internal void UpdateView(Rectangle gameBounds,Frame frame,string inputState,long inputMovements,bool isPaused,bool raise)
        {
            if(!state.IsVisible(options,isPaused))
            {
                Hide();
                return;
            }
            if(layout==null||layout.Viewport!=gameBounds.Size||layout.Section!=state.Section||layout.Collapsed!=state.Collapsed)
            {
                layout=new QuickHudLayout(gameBounds.Size,state);
                pressed=QuickHudAction.None;
                nextStatus=DateTime.MinValue;
                dirty=true;
            }
            viewport=gameBounds;
            if(IsInteracting)
            {
                // A non-activating window cannot depend on capture outside its bounds.
                int leftButton=SystemInformation.MouseButtonsSwapped?2:1;
                if((Native.GetAsyncKeyState(leftButton)&0x8000)==0)pressed=QuickHudAction.None;
                else MovePointer(Cursor.Position);
            }
            Rectangle bounds=layout.Bounds(new Point(options.QuickHudX,options.QuickHudY));
            bounds.Offset(viewport.Location);
            bool moved=Bounds!=bounds;
            Bounds=bounds;
            Point cursor=Cursor.Position;
            SetHover(layout.HitTest(new Point(cursor.X-bounds.X,cursor.Y-bounds.Y)));
            if(paused!=isPaused)
            {
                paused=isPaused;
                dirty=true;
            }
            DateTime now=DateTime.UtcNow;
            if(now>=nextStatus)
            {
                hertz=frame.ReadHertz;
                aimState=inputState;
                movements=inputMovements;
                nearby=!state.Collapsed&&!isPaused?(int?)NearbyPlayers.Count(frame,options):null;
                nextStatus=now.AddMilliseconds(250);
                dirty=true;
            }
            if(Handle==IntPtr.Zero)
            {
                // WinForms can activate toplevel Forms when their bounds change.
                // Own the HWND so every show and move explicitly avoids activation.
                CreateHandle(Native.LayeredOverlay(bounds,L.ProductName,false));
            }
            bool shown=Visible;
            if(dirty||!shown)
            {
                surface.Draw(Handle,bounds,g=>QuickHud.DrawPanel(g,layout,options,state,hover,aimState,hertz,movements,paused,nearby));
                dirty=false;
            }
            if(!shown||raise||moved)Native.PlaceOverlay(Handle,bounds,!shown||raise);
        }

        private void BeginInteraction(Point point)
        {
            if(layout==null||!Visible)return;
            pressed=layout.HitTest(point);
            dragOffset=point;
            MovePointer(new Point(point.X+Bounds.X,point.Y+Bounds.Y));
        }

        private void ReleaseInteraction(Point point)
        {
            if(!IsInteracting)return;
            QuickHudAction action=pressed;
            bool click=action!=QuickHudAction.Move&&!IsSlider(action)&&layout.HitTest(point)==action;
            MovePointer(new Point(point.X+Bounds.X,point.Y+Bounds.Y));
            pressed=QuickHudAction.None;
            if(click&&ClickAction!=null)ClickAction(action);
        }

        private void SetHover(QuickHudAction action)
        {
            if(hover==action)return;
            hover=action;
            dirty=true;
        }

        private static bool IsSlider(QuickHudAction action)
        {
            return action>=QuickHudAction.Strength&&action<=QuickHudAction.MouseScale;
        }

        private void MovePointer(Point screen)
        {
            if(pressed==QuickHudAction.Move)
            {
                Point origin=new Point(screen.X-viewport.X-dragOffset.X,screen.Y-viewport.Y-dragOffset.Y);
                Point clamped=layout.Bounds(origin).Location;
                // A click on the title leaves a saved position from another window size intact.
                if(clamped==layout.Bounds(new Point(options.QuickHudX,options.QuickHudY)).Location)return;
                options.QuickHudX=clamped.X;
                options.QuickHudY=clamped.Y;
                if(PositionChanged!=null)PositionChanged();
            }
            else if(IsSlider(pressed))
            {
                int row=pressed-QuickHudAction.Strength;
                Rectangle track=layout.SliderTrack(pressed);
                float fraction=((screen.X-Bounds.X)/layout.Scale-track.X)/track.Width;
                int oldRow=state.SelectedRow;
                float oldValue=row==0?options.Aim.Strength:row==1?options.Aim.Radius:options.Aim.InputScale;
                state.SelectedRow=row;
                QuickActions.SetAdjustment(options,row,fraction);
                float newValue=row==0?options.Aim.Strength:row==1?options.Aim.Radius:options.Aim.InputScale;
                if(oldValue!=newValue&&AdjustmentChanged!=null)AdjustmentChanged();
                if(oldRow!=row||oldValue!=newValue)dirty=true;
            }
        }

        internal void Hide()
        {
            if(Visible)Native.HideOverlayWindow(Handle);
            pressed=QuickHudAction.None;
            SetHover(QuickHudAction.None);
            nextStatus=DateTime.MinValue;
        }

        public void Dispose()
        {
            if(Handle!=IntPtr.Zero)DestroyHandle();
            surface.Dispose();
        }
    }
}
