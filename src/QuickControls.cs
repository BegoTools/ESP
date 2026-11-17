using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ESPWorkbench
{
    public static class QuickActions
    {
        public static readonly string[] Modes={"Scoped","Firing","Always","ScopedAndFiring","Hold"};
        public static readonly string[] Points={"Head","Chest","Legs"};
        public static string ModeLabel(string mode)
        {
            if(mode=="Scoped")return L.Text("السكوب فقط");if(mode=="Firing")return L.Text("الضرب فقط");if(mode=="Always")return L.Text("دائمًا");if(mode=="ScopedAndFiring")return L.Text("سكوب + ضرب");return L.Text("ضغط زر");
        }
        private static string Next(string[] values,string value) { return values[(Array.IndexOf(values,value)+1)%values.Length]; }
        public static void Apply(RenderOptions options,int functionKey)
        {
            if(functionKey==3)options.Aim.Enabled=!options.Aim.Enabled;
            else if(functionKey==4)options.Aim.Activation=Next(Modes,options.Aim.Activation);
            else if(functionKey==5)options.Aim.AimPoint=Next(Points,options.Aim.AimPoint);
            else if(functionKey==6)options.EspEnabled=!options.EspEnabled;
        }
        public static void Adjust(RenderOptions options,int row,int direction)
        {
            if(row==0)options.Aim.Strength=Math.Max(0,Math.Min(100,options.Aim.Strength+direction*5));
            else if(row==1)options.Aim.Radius=Math.Max(20,Math.Min(1200,options.Aim.Radius+direction*10));
            else if(row==2)options.Aim.InputScale=(float)Math.Round(Math.Max(.1,Math.Min(5,options.Aim.InputScale+direction*.05)),2);
        }
        internal static void SetAdjustment(RenderOptions options,int row,float fraction)
        {
            fraction=Math.Max(0,Math.Min(1,fraction));
            if(row==0)options.Aim.Strength=(int)Math.Round(fraction*100);
            else if(row==1)options.Aim.Radius=(int)Math.Round(20+fraction*1180);
            else if(row==2)options.Aim.InputScale=(float)Math.Round(.1+fraction*4.9,2);
        }
    }
    public enum QuickHudSection { None, Adjustments, Display }

    public sealed class QuickHudState
    {
        public QuickHudSection Section;
        public bool Collapsed,Hidden;
        public int SelectedRow;
        public readonly HashSet<int> Unavailable=new HashSet<int>();
        public bool Open { get { return Section==QuickHudSection.Adjustments; } }

        public bool IsVisible(RenderOptions options,bool paused)
        {
            return !Hidden&&(Section!=QuickHudSection.None||(!paused&&options.QuickHudVisible));
        }

        public void ToggleSection(QuickHudSection section)
        {
            bool close=Section==section&&!Collapsed&&!Hidden;
            Section=close?QuickHudSection.None:section;
            Collapsed=Hidden=false;
        }

        public void ToggleCollapse(RenderOptions options,bool paused)
        {
            if(!IsVisible(options,paused))
            {
                Hidden=Collapsed=false;
                if(!IsVisible(options,paused))Section=QuickHudSection.Adjustments;
            }
            else Collapsed=!Collapsed;
        }
    }

    internal enum QuickHudAction
    {
        None, Move, Collapse, Hide, Aim, Activation, Target, Esp, Adjustments, Display,
        Strength, Radius, MouseScale, Players, Training, Vehicles, Loot, Crates, PlayerCount,
        AimCircle, VisibilityColors, Settings
    }

    internal sealed class QuickHudLayout
    {
        private readonly Dictionary<QuickHudAction,Rectangle> areas=new Dictionary<QuickHudAction,Rectangle>();
        private readonly Size size;
        public readonly Size Viewport,DesignSize;
        public readonly QuickHudSection Section;
        public readonly bool Collapsed;
        public readonly float Scale;
        public readonly int Footer;

        public QuickHudLayout(Size viewport,QuickHudState state)
        {
            Viewport=viewport;
            Collapsed=state.Collapsed;
            Section=state.Section;
            Footer=332+(Section==QuickHudSection.None?0:116);
            DesignSize=Collapsed?new Size(338,46):new Size(352,Footer+54);
            Scale=Math.Min(1f,Math.Min((float)viewport.Width/DesignSize.Width,(float)viewport.Height/DesignSize.Height));
            size=new Size(Math.Min(viewport.Width,(int)Math.Ceiling(DesignSize.Width*Scale)),
                Math.Min(viewport.Height,(int)Math.Ceiling(DesignSize.Height*Scale)));

            int right=DesignSize.Width-10;
            areas.Add(QuickHudAction.Move,new Rectangle(2,2,right-72,Collapsed?42:54));
            areas.Add(QuickHudAction.Collapse,new Rectangle(right-68,9,32,28));
            areas.Add(QuickHudAction.Hide,new Rectangle(right-32,9,32,28));
            if(Collapsed)return;

            for(int row=0;row<4;row++)
                areas.Add(QuickHudAction.Aim+row,new Rectangle(10,82+36*row,332,31));
            areas.Add(QuickHudAction.Adjustments,new Rectangle(10,229,163,29));
            areas.Add(QuickHudAction.Display,new Rectangle(179,229,163,29));
            if(Section==QuickHudSection.Adjustments)
            {
                for(int row=0;row<3;row++)
                    areas.Add(QuickHudAction.Strength+row,new Rectangle(10,262+38*row,332,34));
            }
            if(Section==QuickHudSection.Display)
            {
                for(int index=0;index<QuickHud.DisplayOptions.Length;index++)
                    areas.Add(QuickHud.DisplayOptions[index].Action,new Rectangle(10+169*(index%2),262+28*(index/2),163,25));
            }
            areas.Add(QuickHudAction.Settings,new Rectangle(10,Footer+3,332,26));
        }

        public Rectangle Bounds(Point position)
        {
            int x=Math.Max(0,Math.Min(Viewport.Width-size.Width,position.X));
            int y=Math.Max(0,Math.Min(Viewport.Height-size.Height,position.Y));
            return new Rectangle(new Point(x,y),size);
        }

        public Rectangle Area(QuickHudAction action) { return areas[action]; }

        public Rectangle SliderTrack(QuickHudAction action)
        {
            Rectangle row=Area(action);
            return new Rectangle(row.X+12,row.Bottom-9,row.Width-24,3);
        }

        public QuickHudAction HitTest(Point point)
        {
            if(Scale<=0||point.X<0||point.Y<0)return QuickHudAction.None;
            Point designPoint=new Point((int)(point.X/Scale),(int)(point.Y/Scale));
            foreach(var area in areas)
                if(area.Value.Contains(designPoint))return area.Key;
            return QuickHudAction.None;
        }
    }

    public static class QuickHud
    {
        internal sealed class DisplayOption
        {
            public readonly QuickHudAction Action;
            public readonly string Label;
            public readonly Func<RenderOptions,bool> Get;
            public readonly Action<RenderOptions,bool> Set;

            public DisplayOption(QuickHudAction action,string label,Func<RenderOptions,bool> get,Action<RenderOptions,bool> set)
            { Action=action;Label=label;Get=get;Set=set; }
        }

        internal static readonly DisplayOption[] DisplayOptions={
            new DisplayOption(QuickHudAction.Players,"اللاعبون والبوتات",o=>o.Players.Enabled,(o,v)=>o.Players.Enabled=v),
            new DisplayOption(QuickHudAction.Training,"أهداف التدريب",o=>o.Training.Enabled,(o,v)=>o.Training.Enabled=v),
            new DisplayOption(QuickHudAction.Vehicles,"المركبات",o=>o.Vehicles.Enabled,(o,v)=>o.Vehicles.Enabled=v),
            new DisplayOption(QuickHudAction.Loot,"العناصر والأسلحة",o=>o.Loot.Enabled,(o,v)=>o.Loot.Enabled=v),
            new DisplayOption(QuickHudAction.Crates,"الصناديق",o=>o.Crates.Enabled,(o,v)=>o.Crates.Enabled=v),
            new DisplayOption(QuickHudAction.PlayerCount,"Nearby count",o=>o.PlayerCountVisible,(o,v)=>o.PlayerCountVisible=v),
            new DisplayOption(QuickHudAction.AimCircle,"Aim circle",o=>o.Aim.ShowRadius,(o,v)=>o.Aim.ShowRadius=v),
            new DisplayOption(QuickHudAction.VisibilityColors,"Visibility colors",o=>o.Visibility.Enabled,(o,v)=>o.Visibility.Enabled=v)
        };
        private static readonly Color Row=Color.FromArgb(225,32,43,54),Highlight=Color.FromArgb(235,29,67,82);

        private static void Text(Graphics g,string text,Rectangle bounds,int size,Color color,bool rtl=false,bool bold=false,bool center=false)
        {
            using(Font font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
            using(Brush brush=new SolidBrush(color))
            using(StringFormat format=new StringFormat {
                Alignment=center?StringAlignment.Center:StringAlignment.Near,
                LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,
                FormatFlags=StringFormatFlags.NoWrap|(rtl?StringFormatFlags.DirectionRightToLeft:0)
            })g.DrawString(text,font,brush,bounds,format);
        }

        private static void Button(Graphics g,Rectangle area,Color color,bool active=false)
        {
            using(GraphicsPath path=SpectraTheme.Round(area,7))
            using(Brush brush=new SolidBrush(color))
            using(Pen outline=new Pen(active?Color.FromArgb(170,SpectraTheme.Accent):SpectraTheme.Border))
            { g.FillPath(brush,path);g.DrawPath(outline,path); }
        }

        private static void ActionRow(Graphics g,QuickHudLayout layout,QuickHudAction action,QuickHudAction hover,
            string label,string value,string key,bool unavailable,Color valueColor)
        {
            Rectangle r=layout.Area(action);
            bool toggle=action==QuickHudAction.Aim||action==QuickHudAction.Esp;
            bool enabled=valueColor==SpectraTheme.Green;
            Button(g,r,hover==action?Highlight:Row,enabled);
            Text(g,label,new Rectangle(r.X+10,r.Y,123,r.Height),13,Color.White,L.Rtl,true);
            Text(g,value,new Rectangle(r.X+139,r.Y,toggle?61:111,r.Height),12,valueColor,L.Rtl,true);
            Text(g,key+(unavailable?" !":""),new Rectangle(r.X+(toggle?202:253),r.Y,toggle?88:73,r.Height),11,
                unavailable?Color.Orange:SpectraTheme.Accent,false,false,true);
            if(toggle)SpectraTheme.Toggle(g,r.Right-36,r.Y+8,enabled,27,15);
        }

        public static void Draw(Graphics g,Size viewport,RenderOptions options,QuickHudState state,string aimState,double hertz=0,long movements=0,int? nearby=null)
        {
            if(!state.IsVisible(options,false)||viewport.Width<=0||viewport.Height<=0)return;
            QuickHudLayout layout=new QuickHudLayout(viewport,state);
            Rectangle box=layout.Bounds(new Point(options.QuickHudX,options.QuickHudY));
            GraphicsState saved=g.Save();
            try
            {
                g.TranslateTransform(box.X,box.Y);
                DrawPanel(g,layout,options,state,QuickHudAction.None,aimState,hertz,movements,false,nearby);
            }
            finally { g.Restore(saved); }
        }

        internal static void DrawPanel(Graphics g,QuickHudLayout layout,RenderOptions options,QuickHudState state,
            QuickHudAction hover,string aimState,double hertz,long movements,bool paused,int? nearby)
        {
            g.ScaleTransform(layout.Scale,layout.Scale);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Size size=layout.DesignSize;
            SpectraTheme.Card(g,new RectangleF(2,2,size.Width-4,size.Height-4),true,229,layout.Collapsed?24:13);
            using(Brush dots=new SolidBrush(hover==QuickHudAction.Move?SpectraTheme.Accent:SpectraTheme.Muted))
                for(int i=0;i<6;i++)g.FillEllipse(dots,14+4*(i%2),17+5*(i/2),2f,2f);
            SpectraTheme.Wordmark(g,layout.Collapsed?new RectangleF(28,14,105,18):new RectangleF(29,12,225,32),false);
            string rate=hertz>0?hertz.ToString("0",L.Culture)+" Hz":"—";
            Color statusColor=paused?Color.Gold:SpectraTheme.Green;
            if(layout.Collapsed)
            {
                bool espActive=options.EspEnabled&&!paused;
                Text(g,rate,new Rectangle(145,5,52,34),12,SpectraTheme.Accent);
                Text(g,paused?L.Text("Paused"):"ESP "+L.Text(espActive?"On":"Off"),new Rectangle(204,5,55,34),11,
                    paused||espActive?statusColor:SpectraTheme.Muted,false,true);
            }
            else
            {
                using(Pen divider=new Pen(SpectraTheme.Border))g.DrawLine(divider,11,53,size.Width-11,53);
                using(Brush dot=new SolidBrush(statusColor))g.FillEllipse(dot,14,64,7,7);
                Text(g,L.Text(paused?"Paused":"Live connection"),new Rectangle(27,55,232,24),13,statusColor,L.Rtl,true);
                Text(g,rate,new Rectangle(271,55,64,24),14,SpectraTheme.Accent,false,true,true);
            }

            Rectangle collapse=layout.Area(QuickHudAction.Collapse),hide=layout.Area(QuickHudAction.Hide);
            Button(g,collapse,hover==QuickHudAction.Collapse?Highlight:Row);
            Button(g,hide,hover==QuickHudAction.Hide?Color.FromArgb(99,42,67):Row);
            Text(g,layout.Collapsed?"+":"−",collapse,19,Color.White,false,true,true);
            Text(g,"×",hide,19,Color.White,false,true,true);
            if(layout.Collapsed)return;

            ActionRow(g,layout,QuickHudAction.Aim,hover,L.Text("مساعدة التصويب"),L.Text(options.Aim.Enabled?"On":"Off"),
                options.Shortcuts.Label(4),state.Unavailable.Contains(4),options.Aim.Enabled?SpectraTheme.Green:SpectraTheme.Muted);
            ActionRow(g,layout,QuickHudAction.Activation,hover,L.Text("Activation mode"),QuickActions.ModeLabel(options.Aim.Activation),
                options.Shortcuts.Label(5),state.Unavailable.Contains(5),SpectraTheme.Accent);
            ActionRow(g,layout,QuickHudAction.Target,hover,L.Text("موضع التصويب"),options.Aim.PointLabel,
                options.Shortcuts.Label(6),state.Unavailable.Contains(6),SpectraTheme.Accent);
            ActionRow(g,layout,QuickHudAction.Esp,hover,"ESP",L.Text(options.EspEnabled?"On":"Off"),
                options.Shortcuts.Label(7),state.Unavailable.Contains(7),options.EspEnabled?SpectraTheme.Green:SpectraTheme.Muted);

            Rectangle adjust=layout.Area(QuickHudAction.Adjustments),display=layout.Area(QuickHudAction.Display);
            Button(g,adjust,layout.Section==QuickHudSection.Adjustments||hover==QuickHudAction.Adjustments?Highlight:Row);
            Button(g,display,layout.Section==QuickHudSection.Display||hover==QuickHudAction.Display?Highlight:Row);
            Text(g,L.Text("Adjustments")+"  "+options.Shortcuts.Label(3)+(state.Unavailable.Contains(3)?" !":""),adjust,12,
                state.Unavailable.Contains(3)?Color.Orange:SpectraTheme.Accent,L.Rtl,true,true);
            Text(g,L.Text("Display options"),display,12,SpectraTheme.Accent,L.Rtl,true,true);

            if(layout.Section==QuickHudSection.Adjustments)DrawAdjustments(g,layout,options,state,hover);
            if(layout.Section==QuickHudSection.Display)
            {
                foreach(DisplayOption option in DisplayOptions)
                {
                    Rectangle r=layout.Area(option.Action);
                    Button(g,r,hover==option.Action?Highlight:Row);
                    Text(g,L.Text(option.Label),new Rectangle(r.X+6,r.Y,r.Width-42,r.Height),11,Color.White,L.Rtl);
                    SpectraTheme.Toggle(g,r.Right-32,r.Y+5,option.Get(options),27,15);
                }
            }

            Rectangle nearbyCard=new Rectangle(10,layout.Footer-67,332,64);
            SpectraTheme.Card(g,nearbyCard,true,235,9);
            SpectraTheme.NearbyGraphic(g,new RectangleF(nearbyCard.X+9,nearbyCard.Y+5,82,54));
            Text(g,L.Text("Nearby count"),new Rectangle(nearbyCard.X+96,nearbyCard.Y+8,152,47),15,Color.White,L.Rtl,true);
            Text(g,options.EspEnabled&&options.PlayerCountVisible&&!paused&&nearby.HasValue?nearby.Value.ToString(L.Culture):"—",
                new Rectangle(nearbyCard.Right-70,nearbyCard.Y+7,58,47),26,SpectraTheme.Accent,false,true,true);
            Rectangle settings=layout.Area(QuickHudAction.Settings);
            Button(g,settings,hover==QuickHudAction.Settings?Highlight:Row);
            Text(g,L.Text("Full settings menu"),new Rectangle(settings.X+10,settings.Y,209,settings.Height),12,Color.White,L.Rtl,true);
            Text(g,options.Shortcuts.Label(8)+(state.Unavailable.Contains(8)?" !":""),new Rectangle(settings.Right-101,settings.Y,96,settings.Height),11,
                state.Unavailable.Contains(8)?Color.Orange:SpectraTheme.Muted,false,false,true);
            string status=paused?L.Format("{0}: overlay and assist paused",options.Shortcuts.Label(1)):
                options.Aim.Enabled&&!String.IsNullOrEmpty(aimState)?L.Format("Input {0}  |  {1}",movements,aimState):
                L.Format("{0} Pause · {1} Stop",options.Shortcuts.Label(1),options.Shortcuts.Label(2));
            Text(g,status,new Rectangle(13,layout.Footer+32,size.Width-26,18),11,SpectraTheme.Muted,L.Rtl);
        }

        private static void DrawAdjustments(Graphics g,QuickHudLayout layout,RenderOptions options,QuickHudState state,QuickHudAction hover)
        {
            string[] labels={L.Text("قوة التصحيح"),L.Text("نصف قطر الدائرة"),L.Text("Mouse multiplier")};
            string[] values={options.Aim.Strength+"%",options.Aim.Radius+" px",options.Aim.InputScale.ToString("0.00",L.Culture)+"×"};
            float[] fractions={options.Aim.Strength/100f,(options.Aim.Radius-20)/1180f,(options.Aim.InputScale-.1f)/4.9f};
            using(Brush track=new SolidBrush(SpectraTheme.Border))
            using(Brush fill=new SolidBrush(SpectraTheme.Accent))
            for(int row=0;row<3;row++)
            {
                QuickHudAction action=QuickHudAction.Strength+row;
                Rectangle r=layout.Area(action),bar=layout.SliderTrack(action);
                Button(g,r,hover==action||state.SelectedRow==row?Highlight:Row);
                Text(g,labels[row],new Rectangle(r.X+10,r.Y,r.Width-99,23),12,Color.White,L.Rtl);
                Text(g,values[row],new Rectangle(r.Right-85,r.Y,76,23),12,SpectraTheme.Accent,false,true,true);
                float length=bar.Width*Math.Max(0,Math.Min(1,fractions[row]));
                g.FillRectangle(track,bar);
                g.FillRectangle(fill,bar.X,bar.Y,length,bar.Height);
                g.FillEllipse(fill,bar.X+length-3,bar.Y-2,7,7);
            }
        }
    }
}
