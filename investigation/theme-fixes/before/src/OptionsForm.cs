using System;
using System.Drawing;
using System.Windows.Forms;

namespace ESPWorkbench
{
    public sealed class OptionsForm : Form
    {
        private readonly RenderOptions options;
        private readonly Action save;
        private readonly Panel content=new Panel();
        private readonly ListBox navigation=new ListBox();
        private readonly Color background=SpectraTheme.Background,surface=SpectraTheme.Surface;
        private readonly Func<Control>[] pages;
        private bool rendering;
        protected override bool ShowWithoutActivation { get { return rendering; } }
        public OptionsForm(RenderOptions options,Action save,Action<bool> capture=null)
        {
            options.EnsureDefaults();
            this.options=options; this.save=save;
            Text="SpectraVision / Settings";FormBorderStyle=FormBorderStyle.None;DoubleBuffered=true;Size=new Size(1120,800); MinimumSize=new Size(1000,720);
            StartPosition=FormStartPosition.CenterParent; BackColor=background; ForeColor=Color.Gainsboro; Font=new Font("Segoe UI",11);
            TableLayoutPanel layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Padding=new Padding(18) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,220)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58)); Controls.Add(layout);
            navigation.Dock=DockStyle.Fill; navigation.BackColor=surface; navigation.ForeColor=Color.White; navigation.BorderStyle=BorderStyle.None; navigation.Font=new Font("Segoe UI",13); navigation.ItemHeight=48; navigation.DrawMode=DrawMode.OwnerDrawFixed;
            navigation.Items.AddRange(new object[]{"نظرة عامة","مساعدة التصويب","اللاعبون والبوتات","أهداف التدريب","المركبات","العناصر والأسلحة","الصناديق","الأداء والمظهر","اختصارات الكيبورد"});
            navigation.DrawItem+=delegate(object sender,DrawItemEventArgs e) { if(e.Index<0)return; using(Brush bg=new SolidBrush(e.Index==navigation.SelectedIndex?Color.FromArgb(73,28,113):surface))e.Graphics.FillRectangle(bg,e.Bounds); using(Brush text=new SolidBrush(e.Index==navigation.SelectedIndex?SpectraTheme.Accent:Color.Gainsboro)) e.Graphics.DrawString(navigation.Items[e.Index].ToString(),navigation.Font,text,e.Bounds.Left+14,e.Bounds.Top+12); };
            content.Dock=DockStyle.Fill; content.Margin=new Padding(16,0,0,0); layout.Controls.Add(navigation,0,0); layout.Controls.Add(content,1,0);
            FlowLayoutPanel footer=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,12,0,0) }; layout.Controls.Add(footer,0,1); layout.SetColumnSpan(footer,2);
            Button done=Button("حفظ والعودة للعبة",200); done.Click+=delegate { Close(); }; footer.Controls.Add(done);
            footer.Controls.Add(new Label { Text="التغييرات تطبّق فورًا، وتُحفظ عند إغلاق القائمة.",AutoSize=true,Margin=new Padding(14,8,18,0) });
            pages=new Func<Control>[] { Overview,Aim,Players,()=>Category("أهداف التدريب",options.Training,"تُعرض كأهداف تدريب مستقلة عن اللاعبين الحقيقيين والبوتات."),Vehicles,Loot,()=>Category("الصناديق",options.Crates,"عرض الصناديق التي تم التحقق من نوعها. لا نفترض محتويات صندوق غير مقروءة."),Appearance,()=>new ShortcutEditor(options.Shortcuts,capture) };
            navigation.SelectedIndexChanged+=delegate { ShowPage(navigation.SelectedIndex); }; navigation.SelectedIndex=0;
            FormClosing+=delegate { save(); };
        }
        public void SelectPage(int index) { navigation.SelectedIndex=Math.Max(0,Math.Min(pages.Length-1,index)); }
        public void PrepareEmbedded(int index)
        {
            TableLayoutPanel layout=(TableLayoutPanel)Controls[0];layout.Padding=new Padding(3);layout.ColumnStyles[0].Width=205;MinimumSize=Size.Empty;SelectPage(index);
            foreach(Control control in layout.GetControlFromPosition(0,1).Controls)if(control is Button)control.Text="حفظ والعودة للرئيسية";
        }
        private Button Button(string text,int width) { return new Button { Text=text,Width=width,Height=38,BackColor=surface,ForeColor=Color.White,FlatStyle=FlatStyle.Flat }; }
        private FlowLayoutPanel Page(string title,string description)
        {
            FlowLayoutPanel panel=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(12),BackColor=background };
            panel.Controls.Add(new Label { Text=title,AutoSize=true,Font=new Font("Segoe UI",22,FontStyle.Bold),ForeColor=SpectraTheme.Accent,Margin=new Padding(0,0,0,12) });
            panel.Controls.Add(new Label { Text=description,Width=690,Height=60,ForeColor=Color.Silver,RightToLeft=RightToLeft.Yes,Margin=new Padding(0,0,0,18) }); return panel;
        }
        private void ShowPage(int index) { if(index<0)return; while(content.Controls.Count>0)content.Controls[0].Dispose(); content.Controls.Add(pages[index]()); }
        public void RenderPages(string directory)
        {
            System.IO.Directory.CreateDirectory(directory); rendering=true;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-20000,-20000);Show();Application.DoEvents();
            for(int i=0;i<pages.Length;i++)
            {
                navigation.SelectedIndex=i; PerformLayout(); content.PerformLayout();
                using(Bitmap bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size)); bitmap.Save(System.IO.Path.Combine(directory,"menu-"+i+".png"),System.Drawing.Imaging.ImageFormat.Png); }
            }
            Hide();rendering=false;
        }
        private static void Toggle(Control parent,string text,bool value,Action<bool> changed)
        {
            CheckBox box=new SpectraSwitch { Text=text,Checked=value,Width=650,Height=32,Margin=new Padding(0,3,0,3) }; box.CheckedChanged+=delegate { changed(box.Checked); }; parent.Controls.Add(box);
        }
        private void Number(Control parent,string label,decimal value,decimal min,decimal max,Action<decimal> changed,decimal increment=1)
        {
            FlowLayoutPanel row=new FlowLayoutPanel { Width=680,Height=44,Margin=new Padding(0,6,0,6) };
            row.Controls.Add(new Label { Text=label,Width=370,Height=32,TextAlign=ContentAlignment.MiddleLeft });
            NumericUpDown number=new NumericUpDown { Width=120,Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,value)),Increment=increment,DecimalPlaces=increment<1?1:0,BackColor=surface,ForeColor=Color.White };
            number.ValueChanged+=delegate { changed(number.Value); }; row.Controls.Add(number); parent.Controls.Add(row);
        }
        private Control Overview()
        {
            FlowLayoutPanel p=Page("لوحة التحكم","تحكم مستقل في كل فئة. خيارات هذه الصفحة مفاتيح عامة لباقي الصفحات.");
            Toggle(p,"الرسم العام ESP / "+options.Shortcuts.Label(7),options.EspEnabled,v=>options.EspEnabled=v);
            Toggle(p,"عرض اللاعبين",options.Players.Enabled,v=>options.Players.Enabled=v); Toggle(p,"عرض أهداف التدريب",options.Training.Enabled,v=>options.Training.Enabled=v);
            Toggle(p,"عرض المركبات",options.Vehicles.Enabled,v=>options.Vehicles.Enabled=v); Toggle(p,"عرض الأسلحة والعناصر",options.Loot.Enabled,v=>options.Loot.Enabled=v); Toggle(p,"عرض الصناديق",options.Crates.Enabled,v=>options.Crates.Enabled=v);
            Toggle(p,"المربعات / علامات المواقع",options.Boxes,v=>options.Boxes=v); Toggle(p,"الأسماء والتسميات",options.Names,v=>options.Names=v); Toggle(p,"الصحة",options.Health,v=>options.Health=v);
            Toggle(p,"المسافة",options.Distance,v=>options.Distance=v); Toggle(p,"الخطوط من أعلى الشاشة",options.Lines,v=>options.Lines=v); Toggle(p,"تنبيهات الاتجاه والمسافة",options.Alerts,v=>options.Alerts=v); Toggle(p,"أفراد الفريق",options.Allies,v=>options.Allies=v);
            return p;
        }
        private FlowLayoutPanel Category(string title,CategoryOptions category,string description)
        {
            FlowLayoutPanel p=Page(title,description);
            Toggle(p,"تفعيل هذه الفئة",category.Enabled,v=>category.Enabled=v); Toggle(p,"المربعات / علامات المواقع",category.Boxes,v=>category.Boxes=v); Toggle(p,"الاسم / النوع",category.Names,v=>category.Names=v);
            Toggle(p,"الصحة عند توفرها",category.Health,v=>category.Health=v); Toggle(p,"المسافة",category.Distance,v=>category.Distance=v); Toggle(p,"الخطوط من أعلى الشاشة",category.Lines,v=>category.Lines=v); Toggle(p,"تنبيهات خارج مجال الرؤية",category.Alerts,v=>category.Alerts=v);
            Number(p,"أقصى مسافة لهذه الفئة (م)",(decimal)category.MaxDistance,10,5000,v=>category.MaxDistance=(float)v);
            Number(p,"الحد الأقصى للعناصر المعروضة",category.MaxVisible,1,500,v=>category.MaxVisible=(int)v);
            Button color=Button("اختيار اللون",180); color.BackColor=Color.FromArgb(category.ColorArgb); color.Click+=delegate { using(ColorDialog d=new ColorDialog { Color=Color.FromArgb(category.ColorArgb),FullOpen=true }) if(d.ShowDialog(this)==DialogResult.OK) { category.ColorArgb=d.Color.ToArgb();color.BackColor=d.Color; } }; p.Controls.Add(color); return p;
        }
        private void Slider(Control parent,string title,int value,int min,int max,Action<int> changed,Func<int,string> format)
        {
            FlowLayoutPanel row=new FlowLayoutPanel { Width=680,Height=72,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new Padding(0,3,0,3) };
            Label caption=new Label { Text=title+": "+format(value),Width=650,Height=26 };
            SpectraSlider bar=new SpectraSlider { Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,value)),SmallChange=1,LargeChange=Math.Max(1,(max-min)/20),Width=650,Height=42,Margin=Padding.Empty,AccessibleName=title };
            bar.ValueChanged+=delegate { changed(bar.Value);caption.Text=title+": "+format(bar.Value); };
            row.Controls.Add(caption);row.Controls.Add(bar);parent.Controls.Add(row);
        }
        private void Choice(Control parent,string title,string[] labels,int selected,Action<int> changed)
        {
            FlowLayoutPanel row=new FlowLayoutPanel { Width=680,Height=42,Margin=new Padding(0,4,0,4) };
            row.Controls.Add(new Label { Text=title,Width=340,Height=32 });
            SpectraChoice combo=new SpectraChoice { Width=300,Height=32,BackColor=surface,ForeColor=Color.White,AccessibleName=title };
            combo.Items.AddRange(labels);combo.SelectedIndex=selected;
            combo.SelectedIndexChanged+=delegate { if(combo.SelectedIndex>=0)changed(combo.SelectedIndex); };
            row.Controls.Add(combo);parent.Controls.Add(row);
        }
        private Control Aim()
        {
            AimOptions a=options.Aim;
            FlowLayoutPanel p=Page("مساعدة التصويب / Aim","تصحيح خارجي بحركة الماوس فقط. يُشترط تطابق صلاحيات الأداة مع المحاكي. أوضاع السكوب والضرب تعتمد على حالة اللعبة المقروءة.");
            Toggle(p,"تفعيل مساعدة التصويب",a.Enabled,v=>a.Enabled=v);
            string[] regions={"Head","Chest","Legs"};
            Choice(p,"موضع التصويب",new[]{"الرأس (تقديري)","الصدر (تقديري)","الأرجل (تقديري)"},Array.IndexOf(regions,a.AimPoint),i=>a.AimPoint=regions[i]);
            Slider(p,"حجم الدائرة — نصف القطر",a.Radius,20,1200,v=>a.Radius=v,v=>v+" px عند 1080p");
            Slider(p,"قوة المساعدة",a.Strength,0,100,v=>a.Strength=v,v=>v+"%");
            Slider(p,"معامل حركة الماوس",(int)Math.Round(a.InputScale*100),10,500,v=>a.InputScale=v/100f,v=>(v/100f).ToString("0.00")+"x");
            string[] activation={"Scoped","Firing","Always","ScopedAndFiring","Hold"};
            Choice(p,"طريقة التفعيل",new[]{"عند فتح السكوب فقط","أثناء إطلاق النار فقط","دائمًا داخل الدائرة","السكوب مع إطلاق النار","أثناء الضغط المستمر على زر"},Array.IndexOf(activation,a.Activation),i=>a.Activation=activation[i]);
            int[] keys={0x02,0x05,0x06,0x12};
            Choice(p,"زر التفعيل لوضع الضغط",new[]{"زر الماوس الأيمن / RMB","زر جانبي / Mouse 4","زر جانبي / Mouse 5","Alt"},Array.IndexOf(keys,a.HoldKey),i=>a.HoldKey=keys[i]);
            Number(p,"أقصى مسافة للمساعدة (م)",(decimal)a.MaxDistance,10,500,v=>a.MaxDistance=(float)v,10);
            Toggle(p,"إظهار دائرة نطاق المساعدة",a.ShowRadius,v=>a.ShowRadius=v);
            Toggle(p,"مساعدة على اللاعبين والبوتات (باستثناء الفريق)",a.Players,v=>a.Players=v);
            Toggle(p,"مساعدة على أهداف التدريب",a.Training,v=>a.Training=v);
            Toggle(p,"فحص ظهور المؤشر (اختياري؛ قد يمنع الحركة في GameLoop)",a.RequireCapturedCursor,v=>a.RequireCapturedCursor=v);
            p.Controls.Add(new Label { Text="التقدير من موضع الجسم وارتفاعه؛ لا يتوفر فحص حجب بالجدران. عدّل المفاتيح من صفحة اختصارات الكيبورد. Insert يصغّر ويكبّر لوحة اللعبة.",Width=680,Height=70,RightToLeft=RightToLeft.Yes,ForeColor=Color.Silver,Margin=new Padding(0,14,0,0) });
            return p;
        }
        private Control Players()
        {
            FlowLayoutPanel p=Category("اللاعبون والبوتات",options.Players,"BOT يعتمد على بيانات اللعبة. أهداف التدريب منفصلة، والحالة غير المؤكدة تُعرض UNKNOWN.");
            Toggle(p,"إظهار نوع الشخصية BOT / PLAYER",options.BotLabels,v=>options.BotLabels=v); Toggle(p,"عرض البوتات",options.ShowBots,v=>options.ShowBots=v); Toggle(p,"عرض اللاعبين غير المصنفين AI",options.ShowPlayers,v=>options.ShowPlayers=v); Toggle(p,"عرض الحالة غير المعروفة",options.ShowUnknownPlayers,v=>options.ShowUnknownPlayers=v); return p;
        }
        private Control Vehicles()
        {
            FlowLayoutPanel p=Category("المركبات",options.Vehicles,"صحة المركبة والوقود من VehicleCommon. الوقود يُعرض كنسبة إلى سعة المركبة الفعلية.");
            Toggle(p,"نسبة الوقود",options.Fuel,v=>options.Fuel=v); return p;
        }
        private Control Loot()
        {
            FlowLayoutPanel p=Category("الأسلحة والعناصر",options.Loot,"تصفية مستقلة لتقليل ازدحام ساحة التدريب. أسماء العناصر مأخوذة من الأنواع المرصودة ومعرفاتها.");
            Toggle(p,"الأسلحة",options.LootWeapons,v=>options.LootWeapons=v); Toggle(p,"الذخيرة",options.LootAmmo,v=>options.LootAmmo=v); Toggle(p,"العلاج والمشروبات",options.LootHealing,v=>options.LootHealing=v); Toggle(p,"الخوذات والدروع والحقائب",options.LootArmor,v=>options.LootArmor=v); Toggle(p,"المناظير",options.LootOptics,v=>options.LootOptics=v); Toggle(p,"المعدات والملحقات",options.LootEquipment,v=>options.LootEquipment=v); Toggle(p,"أنواع أخرى",options.LootOther,v=>options.LootOther=v); Toggle(p,"كمية العنصر",options.Quantities,v=>options.Quantities=v); Toggle(p,"معرف العنصر",options.ShowItemIds,v=>options.ShowItemIds=v); return p;
        }
        private Control Appearance()
        {
            FlowLayoutPanel p=Page("الأداء والمظهر","120 Hz هو معدل طلب القراءة والرسم؛ الإطارات المرئية تعتمد أيضًا على اللعبة والشاشة. التنعيم يستخدم البيانات المقروءة فقط.");
            Toggle(p,"إظهار شريط الاختصارات فوق الشمال",options.QuickHudVisible,v=>options.QuickHudVisible=v);
            Number(p,"معدل الرسم المطلوب (Hz)",options.RefreshRate,30,120,v=>options.RefreshRate=(int)v,30);
            Number(p,"حجم الخط",(decimal)options.FontSize,7,18,v=>options.FontSize=(float)v,.5m); Number(p,"سُمك الخطوط والمربعات",(decimal)options.LineWidth,1,4,v=>options.LineWidth=(float)v,.5m); Number(p,"أقصى عدد للتسميات على الشاشة",options.LabelLimit,10,200,v=>options.LabelLimit=(int)v);
            Number(p,"حد عام للمسافة (م)",(decimal)options.MaxDistance,10,5000,v=>options.MaxDistance=(float)v); return p;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);if(TopLevel) { e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;using(System.Drawing.Drawing2D.GraphicsPath path=SpectraTheme.Round(new RectangleF(1,1,Width-3,Height-3),20))using(Pen border=new Pen(SpectraTheme.Border,2))e.Graphics.DrawPath(border,path); }
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
        {
            if(keyData==Keys.Escape&&TopLevel) { Close();return true; }return base.ProcessCmdKey(ref msg,keyData);
        }
    }
}
