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
            if(mode=="Scoped")return "السكوب فقط";if(mode=="Firing")return "الضرب فقط";if(mode=="Always")return "دائمًا";if(mode=="ScopedAndFiring")return "سكوب + ضرب";return "ضغط زر";
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
    }
    public sealed class QuickHudState
    {
        public bool Open;
        public bool Collapsed;
        public int SelectedRow;
        public readonly HashSet<int> Unavailable=new HashSet<int>();
    }
    public static class QuickHud
    {
        public static Rectangle Bounds(Size viewport,bool expanded,bool collapsed=false)
        {
            return new Rectangle(12,12,Math.Min(collapsed?246:310,Math.Max(1,viewport.Width-24)),Math.Min(Math.Max(1,viewport.Height-24),collapsed?36:expanded?322:208));
        }
        private static void Label(Graphics g,string text,Font font,Brush brush,RectangleF bounds,bool rtl)
        {
            using(StringFormat format=new StringFormat { Alignment=rtl?StringAlignment.Far:StringAlignment.Near,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap })g.DrawString(text,font,brush,bounds,format);
        }
        public static void Draw(Graphics g,Size viewport,RenderOptions options,QuickHudState state,string aimState,double hertz=0,long movements=0)
        {
            if(!options.QuickHudVisible&&!state.Open)return;
            Rectangle box=Bounds(viewport,state.Open,state.Collapsed);
            GraphicsState saved=g.Save();
            try
            {
                g.SetClip(new Rectangle(0,0,viewport.Width,viewport.Height));
                SpectraTheme.Card(g,box,true);
                using(Font title=new Font("Segoe UI",10,FontStyle.Bold))
                using(Font font=new Font("Segoe UI",9))
                using(Font small=new Font("Segoe UI",8))
                using(Brush accent=new SolidBrush(SpectraTheme.Accent))
                using(Brush muted=new SolidBrush(SpectraTheme.Muted))
                {
                    if(state.Collapsed)
                    {
                        Label(g,"◆ SpectraVision   |   Insert  ▾",title,accent,new RectangleF(box.X+10,box.Y+5,box.Width-20,26),false);return;
                    }
                    Label(g,"SpectraVision  "+(hertz>0?hertz.ToString("0")+" Hz":""),title,accent,new RectangleF(box.X+12,box.Y+5,box.Width-90,24),false);
                    Label(g,"Insert ▴",small,muted,new RectangleF(box.Right-68,box.Y+6,64,24),false);
                    string[] labels={"التصويب  "+(options.Aim.Enabled?"ON":"OFF"),"التفعيل  "+QuickActions.ModeLabel(options.Aim.Activation),"الموضع  "+options.Aim.PointLabel,"ESP  "+(options.EspEnabled?"ON":"OFF"),state.Open?"إغلاق الإعدادات السريعة":"فتح الإعدادات السريعة"};
                    for(int i=0;i<5;i++)
                    {
                        float y=box.Y+35+i*25;
                        int action=i==4?3:i+4;
                        bool unavailable=state.Unavailable.Contains(action);
                        using(Brush badge=new SolidBrush(Color.FromArgb(38,26,54)))g.FillRectangle(badge,box.X+10,y,90,21);
                        Label(g,options.Shortcuts.Label(action)+(unavailable?" !":""),small,unavailable?Brushes.Orange:accent,new RectangleF(box.X+13,y,86,21),false);
                        Label(g,unavailable?"الاختصار مستخدم":labels[i],font,unavailable?Brushes.Orange:Brushes.Gainsboro,new RectangleF(box.X+106,y,box.Width-119,21),true);
                    }
                    Label(g,options.Aim.Strength+"%  |  "+options.Aim.Radius+" px  |  "+options.Aim.InputScale.ToString("0.00")+"x",small,muted,new RectangleF(box.X+12,box.Y+164,box.Width-24,18),false);
                    Label(g,options.Shortcuts.Label(1)+" Pause  ·  "+options.Shortcuts.Label(2)+" Stop",small,muted,new RectangleF(box.X+12,box.Y+184,box.Width-24,18),false);
                    if(state.Open)
                    {
                        string[] rowLabels={"قوة التصحيح","نصف قطر الدائرة","معامل حركة الماوس"};
                        string[] values={options.Aim.Strength+"%",options.Aim.Radius+" px",options.Aim.InputScale.ToString("0.00")+"x"};
                        float[] fractions={options.Aim.Strength/100f,(options.Aim.Radius-20)/1180f,(options.Aim.InputScale-.1f)/4.9f};
                        for(int row=0;row<3;row++)
                        {
                            float y=box.Y+209+row*29;
                            using(Brush rowBackground=new SolidBrush(row==state.SelectedRow?Color.FromArgb(53,30,74):Color.FromArgb(25,19,35)))g.FillRectangle(rowBackground,box.X+10,y,box.Width-20,26);
                            Label(g,values[row],font,row==state.SelectedRow?accent:Brushes.Gainsboro,new RectangleF(box.X+17,y,85,25),false);
                            Label(g,rowLabels[row],font,Brushes.Gainsboro,new RectangleF(box.X+105,y,box.Width-125,25),true);
                            using(Brush track=new SolidBrush(Color.FromArgb(48,33,64)))g.FillRectangle(track,box.X+17,y+24,box.Width-34,2);
                            g.FillRectangle(accent,box.X+17,y+24,(box.Width-34)*Math.Max(0,Math.Min(1,fractions[row])),2);
                        }
                        Label(g,"↑ ↓ اختيار  |  ← → تعديل  |  "+options.Shortcuts.Label(8)+" القائمة",small,muted,new RectangleF(box.X+12,box.Y+298,box.Width-24,20),true);
                    }
                    if(options.Aim.Enabled&&!String.IsNullOrEmpty(aimState))
                    {
                        RectangleF status=new RectangleF(box.X,box.Bottom+5,box.Width,36);
                        using(Brush bg=new SolidBrush(SpectraTheme.Background))g.FillRectangle(bg,status);
                        Label(g,"INPUT "+movements+"  |  "+aimState,small,muted,new RectangleF(status.X+9,status.Y+3,status.Width-18,status.Height-6),true);
                    }
                }
            }
            finally { g.Restore(saved); }
        }
    }
}
