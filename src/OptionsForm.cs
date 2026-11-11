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
        private readonly Action changed;
        private readonly Func<bool,string> applyImageQuality;
        private bool rendering;
        protected override bool ShowWithoutActivation { get { return rendering; } }
        public OptionsForm(RenderOptions options,Action save,Action<bool> capture=null,Action changed=null,Func<bool,string> applyImageQuality=null)
        {
            options.EnsureDefaults();
            this.options=options; this.save=save;this.changed=changed;
            this.applyImageQuality=applyImageQuality;
            Text=L.ProductName + " | " + L.Text("Settings");FormBorderStyle=FormBorderStyle.None;DoubleBuffered=true;Size=new Size(1120,800); MinimumSize=new Size(1000,720);
            StartPosition=FormStartPosition.CenterParent; BackColor=background; ForeColor=Color.Gainsboro; Font=new Font("Segoe UI",11);
            TableLayoutPanel layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Padding=new Padding(18) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,220)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58)); Controls.Add(layout);
            navigation.Dock=DockStyle.Fill; navigation.BackColor=surface; navigation.ForeColor=Color.White; navigation.BorderStyle=BorderStyle.None; navigation.Font=new Font("Segoe UI",13); navigation.ItemHeight=48; navigation.DrawMode=DrawMode.OwnerDrawFixed;
            navigation.Items.AddRange(new object[]{L.Text("نظرة عامة"),L.Text("مساعدة التصويب"),L.Text("اللاعبون والبوتات"),L.Text("أهداف التدريب"),L.Text("المركبات"),L.Text("العناصر والأسلحة"),L.Text("الصناديق"),L.Text("الأداء والمظهر"),L.Text("اختصارات الكيبورد")});
            navigation.DrawItem+=delegate(object sender,DrawItemEventArgs e) { if(e.Index<0)return; using(Brush bg=new SolidBrush(e.Index==navigation.SelectedIndex?Color.FromArgb(73,28,113):surface))e.Graphics.FillRectangle(bg,e.Bounds); using(Brush text=new SolidBrush(e.Index==navigation.SelectedIndex?SpectraTheme.Accent:Color.Gainsboro)) e.Graphics.DrawString(navigation.Items[e.Index].ToString(),navigation.Font,text,e.Bounds.Left+14,e.Bounds.Top+12); };
            content.Dock=DockStyle.Fill; content.Margin=new Padding(16,0,0,0); layout.Controls.Add(navigation,0,0); layout.Controls.Add(content,1,0);
            FlowLayoutPanel footer=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,12,0,0) }; layout.Controls.Add(footer,0,1); layout.SetColumnSpan(footer,2);
            Button done=Button(L.Text("حفظ والعودة للعبة"),250); done.Click+=delegate { Close(); }; footer.Controls.Add(done);
            footer.Controls.Add(new Label { Text=L.Text("التغييرات تطبّق فورًا، وتُحفظ عند إغلاق القائمة."),AutoSize=true,Margin=new Padding(14,8,18,0) });
            pages=new Func<Control>[] { Overview,Aim,Players,()=>Category(L.Text("أهداف التدريب"),options.Training,L.Text("تُعرض كأهداف تدريب مستقلة عن اللاعبين الحقيقيين والبوتات.")),Vehicles,Loot,()=>Category(L.Text("الصناديق"),options.Crates,L.Text("عرض الصناديق التي تم التحقق من نوعها. لا نفترض محتويات صندوق غير مقروءة."),true),Appearance,()=>new ShortcutEditor(options.Shortcuts,capture,changed) };
            navigation.SelectedIndexChanged+=delegate { ShowPage(navigation.SelectedIndex); }; navigation.SelectedIndex=0;
            FormClosing+=delegate { save(); };
        }
        public void SelectPage(int index) { navigation.SelectedIndex=Math.Max(0,Math.Min(pages.Length-1,index)); }
        public Control CreateEmbeddedPage(int index)
        {
            Control page=pages[Math.Max(0,Math.Min(pages.Length-1,index))]();page.Font=Font;page.ForeColor=ForeColor;
            ObserveChanges(page);return page;
        }
        private void ObserveChanges(Control parent)
        {
            EventHandler notify=delegate { if(changed!=null)changed(); };
            foreach(Control child in parent.Controls)
            {
                CheckBox toggle=child as CheckBox;if(toggle!=null)toggle.CheckedChanged+=notify;
                SpectraSlider slider=child as SpectraSlider;if(slider!=null)slider.ValueChanged+=notify;
                SpectraChoice choice=child as SpectraChoice;if(choice!=null)choice.SelectedIndexChanged+=notify;
                NumericUpDown number=child as NumericUpDown;if(number!=null)number.ValueChanged+=notify;
                ObserveChanges(child);
            }
        }
        public void PrepareEmbedded(int index)
        {
            TableLayoutPanel layout=(TableLayoutPanel)Controls[0];layout.Padding=new Padding(3);layout.ColumnStyles[0].Width=205;MinimumSize=Size.Empty;SelectPage(index);
            foreach(Control control in layout.GetControlFromPosition(0,1).Controls)if(control is Button)control.Text=L.Text("حفظ والعودة للرئيسية");
        }
        private Button Button(string text,int width) { return new Button { Text=text,Width=width,Height=38,BackColor=surface,ForeColor=Color.White,FlatStyle=FlatStyle.Flat }; }
        private FlowLayoutPanel Page(string title,string description)
        {
            FlowLayoutPanel panel=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(22,16,22,20),BackColor=background,Font=new Font("Segoe UI",11),ForeColor=Color.Gainsboro };
            panel.Controls.Add(new Label { Text=title,RightToLeft=L.Direction,AutoSize=true,Font=new Font("Segoe UI",22,FontStyle.Bold),ForeColor=SpectraTheme.Accent,Margin=new Padding(0,0,0,12) });
            panel.Controls.Add(new Label { Text=description,Width=690,Height=60,ForeColor=Color.Silver,RightToLeft=L.Direction,Margin=new Padding(0,0,0,18) });
            panel.SizeChanged+=delegate { ResizePageRows(panel); };return panel;
        }
        private static void ResizePageRows(FlowLayoutPanel panel)
        {
            int width=Math.Max(280,panel.ClientSize.Width-panel.Padding.Horizontal-26);
            foreach(Control child in panel.Controls)
            {
                child.Width=width;
                FlowLayoutPanel row=child as FlowLayoutPanel;if(row==null)continue;
                if(row.FlowDirection==FlowDirection.TopDown) { foreach(Control item in row.Controls)item.Width=Math.Max(100,width-6);continue; }
                if(row.Controls.Count<2)continue;
                int inputWidth=row.Controls[1] is NumericUpDown?120:Math.Min(330,width/2);
                row.WrapContents=false;row.Controls[0].Width=Math.Max(120,width-inputWidth-20);row.Controls[1].Width=inputWidth;
            }
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
            CheckBox box=new SpectraSwitch { Text=text,RightToLeft=L.Direction,Checked=value,Width=650,Height=32,Margin=new Padding(0,3,0,3) }; box.CheckedChanged+=delegate { changed(box.Checked); }; parent.Controls.Add(box);
        }
        private void Number(Control parent,string label,decimal value,decimal min,decimal max,Action<decimal> changed,decimal increment=1)
        {
            FlowLayoutPanel row=new FlowLayoutPanel { Width=680,Height=44,Margin=new Padding(0,6,0,6) };
            row.Controls.Add(new Label { Text=label,RightToLeft=L.Direction,Width=370,Height=32,TextAlign=ContentAlignment.MiddleLeft });
            NumericUpDown number=new NumericUpDown { Width=120,Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,value)),Increment=increment,DecimalPlaces=increment<1?1:0,BackColor=surface,ForeColor=Color.White };
            number.ValueChanged+=delegate { changed(number.Value); }; row.Controls.Add(number); parent.Controls.Add(row);
        }
        private void ColorChoice(Control parent,string title,Func<int> current,Action<int> set)
        {
            FlowLayoutPanel row=new FlowLayoutPanel { Width=680,Height=48,Margin=new Padding(0,3,0,3),WrapContents=false };
            row.Controls.Add(new Label { Text=title,RightToLeft=L.Direction,Width=340,Height=38,TextAlign=ContentAlignment.MiddleLeft });
            Button color=Button("",300);color.AccessibleName=title;color.FlatAppearance.BorderColor=SpectraTheme.Border;
            Action refresh=delegate {
                Color selected=Color.FromArgb(current());color.BackColor=selected;
                color.ForeColor=selected.GetBrightness()>.6f?Color.Black:Color.White;
                color.Text=String.Format("#{0:X2}{1:X2}{2:X2}",selected.R,selected.G,selected.B);
            };
            refresh();
            color.Click+=delegate {
                using(ColorDialog dialog=new ColorDialog { Color=Color.FromArgb(current()),FullOpen=true })
                if(dialog.ShowDialog(parent.FindForm())==DialogResult.OK) { set(dialog.Color.ToArgb());refresh();if(changed!=null)changed(); }
            };
            row.Controls.Add(color);parent.Controls.Add(row);
        }
        private Control Overview()
        {
            FlowLayoutPanel p=Page(L.Text("لوحة التحكم"),L.Text("تحكم مستقل في كل فئة. خيارات هذه الصفحة مفاتيح عامة لباقي الصفحات."));
            Toggle(p,L.Format("الرسم العام ESP / {0}", options.Shortcuts.Label(7)),options.EspEnabled,v=>options.EspEnabled=v);
            Toggle(p,L.Text("عداد اللاعبين حولك أعلى الشاشة"),options.PlayerCountVisible,v=>options.PlayerCountVisible=v);
            p.Controls.Add(new Label { Text=L.Text("يحسب اللاعبين والبوتات وأهداف التدريب الأحياء والمنوكين ضمن مسافات ESP، حتى خلفك وخارج الشاشة ومع إغلاق فلاتر عرضهم. يستبعد فريقك والمركبات والغنائم، ولا يتأثر بحد العناصر المرسومة."),Width=680,Height=68,RightToLeft=L.Direction,ForeColor=SpectraTheme.Muted,Margin=new Padding(0,4,0,12) });
            Toggle(p,L.Text("تغيير ألوان اللاعبين حسب حالة الرؤية"),options.Visibility.Enabled,v=>options.Visibility.Enabled=v);
            ColorChoice(p,L.Text("لون اللاعب الظاهر"),()=>options.Visibility.VisibleColorArgb,v=>options.Visibility.VisibleColorArgb=v);
            ColorChoice(p,L.Text("لون اللاعب المحجوب"),()=>options.Visibility.OccludedColorArgb,v=>options.Visibility.OccludedColorArgb=v);
            ColorChoice(p,L.Text("لون حالة الرؤية غير المعروفة"),()=>options.Visibility.UnknownColorArgb,v=>options.Visibility.UnknownColorArgb=v);
            p.Controls.Add(new Label { Text=L.Text("ألوان الرؤية تخص اللاعبين والبوتات وأهداف التدريب. الحالة غير المعروفة لها لون مستقل، ولا تُعتبر محجوبة. عند تعطيل الخيار تُستخدم ألوان الفئات المعتادة."),Width=680,Height=68,RightToLeft=L.Direction,ForeColor=SpectraTheme.Muted,Margin=new Padding(0,4,0,12) });
            Toggle(p,L.Text("عرض اللاعبين"),options.Players.Enabled,v=>options.Players.Enabled=v); Toggle(p,L.Text("عرض أهداف التدريب"),options.Training.Enabled,v=>options.Training.Enabled=v);
            Toggle(p,L.Text("عرض المركبات"),options.Vehicles.Enabled,v=>options.Vehicles.Enabled=v); Toggle(p,L.InDevelopment("عرض الأسلحة والعناصر"),options.Loot.Enabled,v=>options.Loot.Enabled=v); Toggle(p,L.InDevelopment("عرض الصناديق"),options.Crates.Enabled,v=>options.Crates.Enabled=v);
            Toggle(p,L.Text("المربعات / علامات المواقع"),options.Boxes,v=>options.Boxes=v); Toggle(p,L.Text("الأسماء والتسميات"),options.Names,v=>options.Names=v); Toggle(p,L.Text("الصحة"),options.Health,v=>options.Health=v);
            Toggle(p,L.Text("المسافة"),options.Distance,v=>options.Distance=v); Toggle(p,L.Text("الخطوط من أعلى الشاشة"),options.Lines,v=>options.Lines=v); Toggle(p,L.Text("تنبيهات الاتجاه والمسافة"),options.Alerts,v=>options.Alerts=v); Toggle(p,L.InDevelopment("أفراد الفريق"),options.Allies,v=>options.Allies=v);
            return p;
        }
        private FlowLayoutPanel Category(string title,CategoryOptions category,string description, bool developing=false)
        {
            FlowLayoutPanel p=Page(title,description);
            Toggle(p, developing ? L.InDevelopment("تفعيل هذه الفئة") : L.Text("تفعيل هذه الفئة"),category.Enabled,v=>category.Enabled=v); Toggle(p,L.Text("المربعات / علامات المواقع"),category.Boxes,v=>category.Boxes=v); Toggle(p,L.Text("الاسم / النوع"),category.Names,v=>category.Names=v);
            Toggle(p,L.Text("الصحة عند توفرها"),category.Health,v=>category.Health=v); Toggle(p,L.Text("المسافة"),category.Distance,v=>category.Distance=v); Toggle(p,L.Text("الخطوط من أعلى الشاشة"),category.Lines,v=>category.Lines=v); Toggle(p,L.Text("تنبيهات خارج مجال الرؤية"),category.Alerts,v=>category.Alerts=v);
            Number(p,L.Text("أقصى مسافة لهذه الفئة (م)"),(decimal)category.MaxDistance,10,5000,v=>category.MaxDistance=(float)v);
            Number(p,L.Text("الحد الأقصى للعناصر المعروضة"),category.MaxVisible,1,500,v=>category.MaxVisible=(int)v);
            Button color=Button(L.Text("اختيار اللون"),220); color.BackColor=Color.FromArgb(category.ColorArgb); color.Click+=delegate { using(ColorDialog d=new ColorDialog { Color=Color.FromArgb(category.ColorArgb),FullOpen=true }) if(d.ShowDialog(p.FindForm())==DialogResult.OK) { category.ColorArgb=d.Color.ToArgb();color.BackColor=d.Color;if(changed!=null)changed(); } }; p.Controls.Add(color); return p;
        }
        private void Slider(Control parent,string title,int value,int min,int max,Action<int> changed,Func<int,string> format)
        {
            FlowLayoutPanel row=new FlowLayoutPanel { Width=680,Height=72,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=new Padding(0,3,0,3) };
            Panel captionRow=new Panel { Width=650,Height=26,Margin=Padding.Empty,BackColor=background };
            Label caption=new Label { Text=title,RightToLeft=L.Direction,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft };
            Label currentValue=new Label { Text=format(value),Dock=DockStyle.Right,Width=195,TextAlign=ContentAlignment.MiddleRight,RightToLeft=RightToLeft.No,ForeColor=SpectraTheme.Muted };
            captionRow.Controls.Add(caption);captionRow.Controls.Add(currentValue);
            SpectraSlider bar=new SpectraSlider { Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,value)),SmallChange=1,LargeChange=Math.Max(1,(max-min)/20),Width=650,Height=42,Margin=Padding.Empty,AccessibleName=title };
            bar.ValueChanged+=delegate { changed(bar.Value);currentValue.Text=format(bar.Value); };
            row.Controls.Add(captionRow);row.Controls.Add(bar);parent.Controls.Add(row);
        }
        private void Choice(Control parent,string title,string[] labels,int selected,Action<int> changed)
        {
            FlowLayoutPanel row=new FlowLayoutPanel { Width=680,Height=42,Margin=new Padding(0,4,0,4) };
            row.Controls.Add(new Label { Text=title,RightToLeft=L.Direction,Width=340,Height=32 });
            SpectraChoice combo=new SpectraChoice { Width=300,Height=32,BackColor=surface,ForeColor=Color.White,RightToLeft=L.Direction,AccessibleName=title };
            combo.Items.AddRange(labels);combo.SelectedIndex=selected;
            combo.SelectedIndexChanged+=delegate { if(combo.SelectedIndex>=0)changed(combo.SelectedIndex); };
            row.Controls.Add(combo);parent.Controls.Add(row);
        }
        private Control Aim()
        {
            AimOptions a=options.Aim;
            FlowLayoutPanel p=Page(L.Text("مساعدة التصويب / Aim"),L.Text("تصحيح خارجي بحركة الماوس فقط. يُشترط تطابق صلاحيات الأداة مع المحاكي. أوضاع السكوب والضرب تعتمد على حالة اللعبة المقروءة."));
            Toggle(p,L.Text("تفعيل مساعدة التصويب"),a.Enabled,v=>a.Enabled=v);
            string[] regions={"Head","Chest","Legs"};
            Choice(p,L.Text("موضع التصويب"),new[]{L.Text("الرأس (تقديري)"),L.Text("الصدر (تقديري)"),L.Text("الأرجل (تقديري)")},Array.IndexOf(regions,a.AimPoint),i=>a.AimPoint=regions[i]);
            Slider(p,L.Text("حجم الدائرة — نصف القطر"),a.Radius,20,1200,v=>a.Radius=v,v=>v+" px (1080p)");
            Slider(p,L.Text("قوة المساعدة"),a.Strength,0,100,v=>a.Strength=v,v=>v+"%");
            Slider(p,L.Text("معامل حركة الماوس"),(int)Math.Round(a.InputScale*100),10,500,v=>a.InputScale=v/100f,v=>(v/100f).ToString("0.00", L.Culture)+"x");
            string[] activation={"Scoped","Firing","Always","ScopedAndFiring","Hold"};
            Choice(p,L.Text("طريقة التفعيل"),new[]{L.Text("عند فتح السكوب فقط"),L.Text("أثناء إطلاق النار فقط"),L.Text("دائمًا داخل الدائرة"),L.Text("السكوب مع إطلاق النار"),L.Text("أثناء الضغط المستمر على زر")},Array.IndexOf(activation,a.Activation),i=>a.Activation=activation[i]);
            int[] keys={0x02,0x05,0x06,0x12};
            Choice(p,L.Text("زر التفعيل لوضع الضغط"),new[]{L.Text("زر الماوس الأيمن / RMB"),L.Text("زر جانبي / Mouse 4"),L.Text("زر جانبي / Mouse 5"),"Alt"},Array.IndexOf(keys,a.HoldKey),i=>a.HoldKey=keys[i]);
            Number(p,L.Text("أقصى مسافة للمساعدة (م)"),(decimal)a.MaxDistance,10,500,v=>a.MaxDistance=(float)v,10);
            Toggle(p,L.Text("إظهار دائرة نطاق المساعدة"),a.ShowRadius,v=>a.ShowRadius=v);
            Toggle(p,L.Text("مساعدة على اللاعبين والبوتات (باستثناء الفريق)"),a.Players,v=>a.Players=v);
            Toggle(p,L.Text("مساعدة على أهداف التدريب"),a.Training,v=>a.Training=v);
            Toggle(p,L.Text("فحص ظهور المؤشر (اختياري؛ قد يمنع الحركة في GameLoop)"),a.RequireCapturedCursor,v=>a.RequireCapturedCursor=v);
            p.Controls.Add(new Label { Text=L.Text("مواضع التصويب تقديرية من موضع الجسم وارتفاعه. عدّل المفاتيح من صفحة اختصارات الكيبورد. Insert يصغّر ويكبّر لوحة اللعبة."),Width=680,Height=70,RightToLeft=L.Direction,ForeColor=Color.Silver,Margin=new Padding(0,14,0,0) });
            return p;
        }
        private Control Players()
        {
            FlowLayoutPanel p=Category(L.Text("اللاعبون والبوتات"),options.Players,L.Text("BOT يعتمد على بيانات اللعبة. أهداف التدريب منفصلة، والحالة غير المؤكدة تُعرض UNKNOWN."));
            Toggle(p,L.Text("إظهار نوع الشخصية BOT / PLAYER"),options.BotLabels,v=>options.BotLabels=v); Toggle(p,L.Text("عرض البوتات"),options.ShowBots,v=>options.ShowBots=v); Toggle(p,L.Text("عرض اللاعبين غير المصنفين AI"),options.ShowPlayers,v=>options.ShowPlayers=v); Toggle(p,L.Text("عرض الحالة غير المعروفة"),options.ShowUnknownPlayers,v=>options.ShowUnknownPlayers=v); return p;
        }
        private Control Vehicles()
        {
            FlowLayoutPanel p=Category(L.Text("المركبات"),options.Vehicles,L.Text("صحة المركبة والوقود من VehicleCommon. الوقود يُعرض كنسبة إلى سعة المركبة الفعلية."));
            Toggle(p,L.Text("نسبة الوقود"),options.Fuel,v=>options.Fuel=v); return p;
        }
        private Control Loot()
        {
            FlowLayoutPanel p=Category(L.Text("الأسلحة والعناصر"),options.Loot,L.Text("تصفية مستقلة لتقليل ازدحام ساحة التدريب. أسماء العناصر مأخوذة من الأنواع المرصودة ومعرفاتها."),true);
            Toggle(p,L.Text("الأسلحة"),options.LootWeapons,v=>options.LootWeapons=v); Toggle(p,L.Text("الذخيرة"),options.LootAmmo,v=>options.LootAmmo=v); Toggle(p,L.Text("العلاج والمشروبات"),options.LootHealing,v=>options.LootHealing=v); Toggle(p,L.Text("الخوذات والدروع والحقائب"),options.LootArmor,v=>options.LootArmor=v); Toggle(p,L.Text("المناظير"),options.LootOptics,v=>options.LootOptics=v); Toggle(p,L.Text("المعدات والملحقات"),options.LootEquipment,v=>options.LootEquipment=v); Toggle(p,L.Text("أنواع أخرى"),options.LootOther,v=>options.LootOther=v); Toggle(p,L.Text("كمية العنصر"),options.Quantities,v=>options.Quantities=v); Toggle(p,L.Text("معرف العنصر"),options.ShowItemIds,v=>options.ShowItemIds=v); return p;
        }
        private Control Appearance()
        {
            FlowLayoutPanel p=Page(L.Text("الأداء والمظهر"),L.Text("120 Hz هو معدل طلب القراءة والرسم؛ الإطارات المرئية تعتمد أيضًا على اللعبة والشاشة. التنعيم يستخدم البيانات المقروءة فقط."));
            CheckBox quality=new SpectraSwitch { Text=L.InDevelopment("تحسين الصورة: NVIDIA FXAA للمحاكيات + تنعيم الأداة"),RightToLeft=L.Direction,Checked=options.ImageQualityEnabled,Width=680,Height=42,AccessibleName=L.InDevelopment("تحسين الصورة: NVIDIA FXAA للمحاكيات + تنعيم الأداة"),Enabled=applyImageQuality!=null };
            Label qualityStatus=new Label { Text=options.ImageQualityEnabled?L.Text("تنعيم الأداة مفعّل. إعداد FXAA محفوظ؛ تأثيره على اللعبة غير مؤكد."):L.Text("تحسين الصورة متوقف."),Width=680,Height=64,RightToLeft=L.Direction,ForeColor=SpectraTheme.Muted };
            bool updating=false;
            quality.CheckedChanged+=delegate {
                if(updating)return;
                try { qualityStatus.Text=applyImageQuality(quality.Checked); }
                catch(Exception ex) { updating=true;quality.Checked=options.ImageQualityEnabled;updating=false;qualityStatus.Text=L.Format("تعذر التطبيق؛ لم يتغير الاختيار. {0}", ex.Message); }
            };
            p.Controls.Add(quality);p.Controls.Add(qualityStatus);
            p.Controls.Add(new Label { Text=L.Text("FXAA يُطلب من تعريف NVIDIA لـ GameLoop وLDPlayer. أعد تشغيل المحاكي بعد التفعيل أو الإيقاف؛ الاستجابة تعتمد على دعمه وطريقة الرسم، ولا يمكن تأكيدها من هنا. تنعيم الأداة يطبق فورًا.\nDLSS ورفع الدقة: غير متاحين هنا؛ يحتاج DLSS دعم محرك اللعبة، وNVIDIA Image Scaling يحتاج إعدادات العرض المناسبة. لا يضمن هذا الخيار زيادة FPS أو أفضل نتيجة لكل لعبة."),Width=680,Height=118,RightToLeft=L.Direction,ForeColor=SpectraTheme.Muted,Margin=new Padding(0,3,0,12) });
            Toggle(p,L.Text("إظهار شريط الاختصارات فوق الشمال"),options.QuickHudVisible,v=>options.QuickHudVisible=v);
            Number(p,L.Text("معدل الرسم المطلوب (Hz)"),options.RefreshRate,30,120,v=>options.RefreshRate=(int)v,30);
            Number(p,L.Text("حجم الخط"),(decimal)options.FontSize,7,18,v=>options.FontSize=(float)v,.5m); Number(p,L.Text("سُمك الخطوط والمربعات"),(decimal)options.LineWidth,1,4,v=>options.LineWidth=(float)v,.5m); Number(p,L.Text("أقصى عدد للتسميات على الشاشة"),options.LabelLimit,10,200,v=>options.LabelLimit=(int)v);
            Number(p,L.Text("حد عام للمسافة (م)"),(decimal)options.MaxDistance,10,5000,v=>options.MaxDistance=(float)v); return p;
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
