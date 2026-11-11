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
        public int SelectedRow;
        public readonly HashSet<int> Unavailable=new HashSet<int>();
    }
    public static class QuickHud
    {
        public static Rectangle Bounds(Size viewport,bool expanded)
        {
            return new Rectangle(16,16,Math.Min(360,Math.Max(100,viewport.Width-32)),expanded?350:194);
        }
        private static void Label(Graphics g,string text,Font font,Brush brush,RectangleF bounds,bool rtl)
        {
            using(StringFormat format=new StringFormat { Alignment=rtl?StringAlignment.Far:StringAlignment.Near,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap })g.DrawString(text,font,brush,bounds,format);
        }
        public static void Draw(Graphics g,Size viewport,RenderOptions options,QuickHudState state,string aimState,double hertz=0,long movements=0)
        {
            if(!options.QuickHudVisible&&!state.Open)return;
            Rectangle box=Bounds(viewport,state.Open);
            GraphicsState saved=g.Save();
            try
            {
                g.SetClip(new Rectangle(0,0,viewport.Width,viewport.Height));
                using(Brush background=new SolidBrush(Color.FromArgb(15,23,34)))g.FillRectangle(background,box);
                using(Pen border=new Pen(Color.FromArgb(53,78,88)))g.DrawRectangle(border,box);
                using(Font title=new Font("Segoe UI",10,FontStyle.Bold))
                using(Font font=new Font("Segoe UI",10))
                using(Font small=new Font("Segoe UI",8))
                using(Brush accent=new SolidBrush(Painter.Accent))
                using(Brush muted=new SolidBrush(Color.FromArgb(153,170,185)))
                {
                    Label(g,"QUICK CONTROLS"+(hertz>0?"  /  "+hertz.ToString("0")+" Hz":""),title,accent,new RectangleF(box.X+12,box.Y+6,box.Width-24,24),false);
                    string[] labels={"التصويب  "+(options.Aim.Enabled?"ON":"OFF"),"التفعيل  "+QuickActions.ModeLabel(options.Aim.Activation),"الموضع  "+options.Aim.PointLabel,"ESP  "+(options.EspEnabled?"ON":"OFF"),state.Open?"إغلاق الإعدادات السريعة":"فتح الإعدادات السريعة"};
                    for(int i=0;i<5;i++)
                    {
                        float y=box.Y+34+i*25;
                        bool unavailable=state.Unavailable.Contains(i+3);
                        using(Brush badge=new SolidBrush(Color.FromArgb(31,47,61)))g.FillRectangle(badge,box.X+12,y,40,21);
                        Label(g,"F"+(i+3)+(unavailable?" !":""),title,unavailable?Brushes.Orange:accent,new RectangleF(box.X+15,y,39,21),false);
                        Label(g,unavailable?"الاختصار مستخدم في برنامج آخر":labels[i],font,unavailable?Brushes.Orange:Brushes.Gainsboro,new RectangleF(box.X+60,y,box.Width-74,21),true);
                    }
                    Label(g,options.Aim.Strength+"%  |  "+options.Aim.Radius+" px  |  "+options.Aim.InputScale.ToString("0.00")+"x",small,muted,new RectangleF(box.X+12,box.Y+164,box.Width-24,22),false);
                    if(state.Open)
                    {
                        string[] rowLabels={"قوة التصحيح","نصف قطر الدائرة","معامل حركة الماوس"};
                        string[] values={options.Aim.Strength+"%",options.Aim.Radius+" px",options.Aim.InputScale.ToString("0.00")+"x"};
                        float[] fractions={options.Aim.Strength/100f,(options.Aim.Radius-20)/1180f,(options.Aim.InputScale-.1f)/4.9f};
                        for(int row=0;row<3;row++)
                        {
                            float y=box.Y+194+row*40;
                            using(Brush rowBackground=new SolidBrush(row==state.SelectedRow?Color.FromArgb(26,59,62):Color.FromArgb(21,32,45)))g.FillRectangle(rowBackground,box.X+10,y,box.Width-20,36);
                            Label(g,values[row],font,row==state.SelectedRow?accent:Brushes.Gainsboro,new RectangleF(box.X+17,y,85,25),false);
                            Label(g,rowLabels[row],font,Brushes.Gainsboro,new RectangleF(box.X+105,y,box.Width-125,25),true);
                            using(Brush track=new SolidBrush(Color.FromArgb(48,66,81)))g.FillRectangle(track,box.X+17,y+29,box.Width-34,3);
                            g.FillRectangle(accent,box.X+17,y+29,(box.Width-34)*Math.Max(0,Math.Min(1,fractions[row])),3);
                        }
                        Label(g,"↑ ↓ اختيار   |   ← → تعديل   |   Ctrl+F7 القائمة الكاملة",small,muted,new RectangleF(box.X+12,box.Y+319,box.Width-24,25),true);
                    }
                    if(options.Aim.Enabled&&!String.IsNullOrEmpty(aimState))
                    {
                        RectangleF status=new RectangleF(box.X,box.Bottom+5,box.Width,36);
                        using(Brush bg=new SolidBrush(Color.FromArgb(15,23,34)))g.FillRectangle(bg,status);
                        Label(g,"INPUT "+movements+"  |  "+aimState,small,muted,new RectangleF(status.X+9,status.Y+3,status.Width-18,status.Height-6),true);
                    }
                }
            }
            finally { g.Restore(saved); }
        }
    }
}
