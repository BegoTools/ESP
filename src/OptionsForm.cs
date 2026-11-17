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
            navigation.DrawItem+=delegate(object sender,DrawItemEventArgs e) { if(e.Index<0)return; using(Brush bg=new SolidBrush(e.Index==navigation.SelectedIndex?SpectraTheme.Selected:surface))e.Graphics.FillRectangle(bg,e.Bounds); using(Brush text=new SolidBrush(e.Index==navigation.SelectedIndex?SpectraTheme.Accent:Color.Gainsboro)) e.Graphics.DrawString(navigation.Items[e.Index].ToString(),navigation.Font,text,e.Bounds.Left+14,e.Bounds.Top+12); };
            content.Dock=DockStyle.Fill; content.Margin=new Padding(16,0,0,0); layout.Controls.Add(navigation,0,0); layout.Controls.Add(content,1,0);
            FlowLayoutPanel footer=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,12,0,0) }; layout.Controls.Add(footer,0,1); layout.SetColumnSpan(footer,2);
            Button done=Button(L.Text("حفظ والعودة للعبة"),250); done.Click+=delegate { Close(); }; footer.Controls.Add(done);
            footer.Controls.Add(new Label { Text=L.Text("التغييرات تطبّق فورًا، وتُحفظ عند إغلاق القائمة."),AutoSize=true,Margin=new Padding(14,8,18,0) });
            pages=new Func<Control>[] { Overview,Aim,Players,()=>Category(L.Text("أهداف التدريب"),options.Training,L.Text("تُعرض كأهداف تدريب مستقلة عن اللاعبين الحقيقيين والبوتات.")),Vehicles,Loot,()=>Category(L.Text("الصناديق"),options.Crates,L.Text("عرض الصناديق التي تم التحقق من نوعها. لا نفترض محتويات صندوق غير مقروءة."),null,true),Appearance,()=>ShortcutEditor.Create(options.Shortcuts,capture,changed) };
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
                SpectraNumber number=child as SpectraNumber;if(number!=null)number.ValueChanged+=notify;
                ObserveChanges(child);
            }
        }
        public void PrepareEmbedded(int index)
        {
            TableLayoutPanel layout=(TableLayoutPanel)Controls[0];layout.Padding=new Padding(3);layout.ColumnStyles[0].Width=205;MinimumSize=Size.Empty;SelectPage(index);
            foreach(Control control in layout.GetControlFromPosition(0,1).Controls)if(control is Button)control.Text=L.Text("حفظ والعودة للرئيسية");
        }
        private Button Button(string text,int width) { return new SpectraButton { Text=text,Width=width,Height=38 }; }
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
        private static void Toggle(SpectraCard parent,string text,bool value,Action<bool> changed)
        {
            CheckBox box=new SpectraSwitch { Text=text,RightToLeft=L.Direction,Checked=value,Height=34,Margin=new Padding(0,2,0,2) }; box.CheckedChanged+=delegate { changed(box.Checked); }; parent.Add(box);
        }
        private void Number(SpectraCard parent,string label,decimal value,decimal min,decimal max,Action<decimal> changed,decimal increment=1)
        {
            SpectraNumber number=new SpectraNumber(value,min,max,increment,label);
            number.ValueChanged+=delegate { changed(number.Value); };
            parent.Add(SpectraFields.Pair(SpectraFields.Caption(label),number,144,38));
        }
        private void ColorChoice(SpectraCard parent,string title,Func<int> current,Action<int> set)
        {
            SpectraButton color=new SpectraButton { AccessibleName=title };
            Action refresh=delegate {
                Color selected=Color.FromArgb(current());
                color.Swatch=selected;
                color.Text=String.Format("#{0:X2}{1:X2}{2:X2}",selected.R,selected.G,selected.B);
            };
            refresh();
            color.Click+=delegate {
                using(ColorDialog dialog=new ColorDialog { Color=Color.FromArgb(current()),FullOpen=true })
                if(dialog.ShowDialog(parent.FindForm())==DialogResult.OK) { set(dialog.Color.ToArgb());refresh();if(changed!=null)changed(); }
            };
            parent.Add(SpectraFields.Pair(SpectraFields.Caption(title),color,120,39));
        }
        private Control Overview()
        {
            var page=new SpectraPage(L.Text("لوحة التحكم"),L.Text("تحكم مستقل في كل فئة. خيارات هذه الصفحة مفاتيح عامة لباقي الصفحات."));
            SpectraCard overlay=page.Group("ESP");
            Toggle(overlay,L.Format("الرسم العام ESP / {0}",options.Shortcuts.Label(7)),options.EspEnabled,v=>options.EspEnabled=v);
            Toggle(overlay,L.Text("Nearby count"),options.PlayerCountVisible,v=>options.PlayerCountVisible=v);
            overlay.Add(SpectraFields.Note(L.Text("يحسب اللاعبين والبوتات وأهداف التدريب الأحياء والمنوكين ضمن مسافات ESP، حتى خلفك وخارج الشاشة ومع إغلاق فلاتر عرضهم. يستبعد فريقك والمركبات والغنائم، ولا يتأثر بحد العناصر المرسومة.")));
            SpectraCard visuals=page.Group(L.Text("Display options"));
            Toggle(visuals,L.Text("المربعات / علامات المواقع"),options.Boxes,v=>options.Boxes=v);
            Toggle(visuals,L.Text("الأسماء والتسميات"),options.Names,v=>options.Names=v);
            Toggle(visuals,L.Text("الصحة"),options.Health,v=>options.Health=v);
            Toggle(visuals,L.Text("المسافة"),options.Distance,v=>options.Distance=v);
            Toggle(visuals,L.Text("الخطوط من أعلى الشاشة"),options.Lines,v=>options.Lines=v);
            Toggle(visuals,L.Text("تنبيهات الاتجاه والمسافة"),options.Alerts,v=>options.Alerts=v);
            Toggle(visuals,L.InDevelopment("أفراد الفريق"),options.Allies,v=>options.Allies=v);

            SpectraCard visibility=page.Group(L.Text("Visibility colors"),1);
            Toggle(visibility,L.Text("تغيير ألوان اللاعبين حسب حالة الرؤية"),options.Visibility.Enabled,v=>options.Visibility.Enabled=v);
            ColorChoice(visibility,L.Text("لون اللاعب الظاهر"),()=>options.Visibility.VisibleColorArgb,v=>options.Visibility.VisibleColorArgb=v);
            ColorChoice(visibility,L.Text("لون اللاعب المحجوب"),()=>options.Visibility.OccludedColorArgb,v=>options.Visibility.OccludedColorArgb=v);
            ColorChoice(visibility,L.Text("لون حالة الرؤية غير المعروفة"),()=>options.Visibility.UnknownColorArgb,v=>options.Visibility.UnknownColorArgb=v);
            visibility.Add(SpectraFields.Note(L.Text("ألوان الرؤية تخص اللاعبين والبوتات وأهداف التدريب. الحالة غير المعروفة لها لون مستقل، ولا تُعتبر محجوبة. عند تعطيل الخيار تُستخدم ألوان الفئات المعتادة.")));
            SpectraCard categories=page.Group(L.Text("Categories"),1);
            Toggle(categories,L.Text("عرض اللاعبين"),options.Players.Enabled,v=>options.Players.Enabled=v);
            Toggle(categories,L.Text("عرض أهداف التدريب"),options.Training.Enabled,v=>options.Training.Enabled=v);
            Toggle(categories,L.Text("عرض المركبات"),options.Vehicles.Enabled,v=>options.Vehicles.Enabled=v);
            Toggle(categories,L.InDevelopment("عرض الأسلحة والعناصر"),options.Loot.Enabled,v=>options.Loot.Enabled=v);
            Toggle(categories,L.InDevelopment("عرض الصناديق"),options.Crates.Enabled,v=>options.Crates.Enabled=v);
            return page;
        }
        private SpectraPage Category(string title,CategoryOptions category,string description,Action<SpectraCard> filters=null,bool developing=false)
        {
            var page=new SpectraPage(title,description);
            SpectraCard display=page.Group(L.Text("Display options"));
            Toggle(display,developing?L.InDevelopment("تفعيل هذه الفئة"):L.Text("تفعيل هذه الفئة"),category.Enabled,v=>category.Enabled=v);
            Toggle(display,L.Text("المربعات / علامات المواقع"),category.Boxes,v=>category.Boxes=v);
            Toggle(display,L.Text("الاسم / النوع"),category.Names,v=>category.Names=v);
            Toggle(display,L.Text("الصحة عند توفرها"),category.Health,v=>category.Health=v);
            Toggle(display,L.Text("المسافة"),category.Distance,v=>category.Distance=v);
            Toggle(display,L.Text("الخطوط من أعلى الشاشة"),category.Lines,v=>category.Lines=v);
            Toggle(display,L.Text("تنبيهات خارج مجال الرؤية"),category.Alerts,v=>category.Alerts=v);
            SpectraCard limits=page.Group(L.Text("Limits and color"),1);
            Number(limits,L.Text("أقصى مسافة لهذه الفئة (م)"),(decimal)category.MaxDistance,10,5000,v=>category.MaxDistance=(float)v);
            Number(limits,L.Text("الحد الأقصى للعناصر المعروضة"),category.MaxVisible,1,500,v=>category.MaxVisible=(int)v);
            ColorChoice(limits,L.Text("اختيار اللون"),()=>category.ColorArgb,v=>category.ColorArgb=v);
            if(filters!=null)filters(page.Group(L.Text("Filters"),1));
            return page;
        }
        private void Slider(SpectraCard parent,string title,int value,int min,int max,Action<int> changed,Func<int,string> format)
        {
            Label currentValue=new Label { Text=format(value),TextAlign=ContentAlignment.MiddleRight,RightToLeft=RightToLeft.No,ForeColor=SpectraTheme.Accent };
            SpectraSlider bar=new SpectraSlider { Minimum=min,Maximum=max,Value=Math.Max(min,Math.Min(max,value)),SmallChange=1,LargeChange=Math.Max(1,(max-min)/20),Height=33,Margin=new Padding(0,0,0,8),AccessibleName=title };
            bar.ValueChanged+=delegate { changed(bar.Value);currentValue.Text=format(bar.Value); };
            parent.Add(SpectraFields.Pair(SpectraFields.Caption(title),currentValue,115,25));
            parent.Add(bar);
        }
        private void Choice(SpectraCard parent,string title,string[] labels,int selected,Action<int> changed)
        {
            SpectraChoice combo=new SpectraChoice { RightToLeft=L.Direction,AccessibleName=title,WrapSelection=true };
            combo.Items.AddRange(labels);combo.SelectedIndex=selected;
            combo.SelectedIndexChanged+=delegate { if(combo.SelectedIndex>=0)changed(combo.SelectedIndex); };
            parent.Add(SpectraFields.Pair(SpectraFields.Caption(title),combo,228,38));
        }
        private Control Aim()
        {
            AimOptions a=options.Aim;
            var page=new SpectraPage(L.Text("مساعدة التصويب / Aim"),L.Text("تصحيح خارجي بحركة الماوس فقط. يُشترط تطابق صلاحيات الأداة مع المحاكي. أوضاع السكوب والضرب تعتمد على حالة اللعبة المقروءة."));
            SpectraCard p=page.Group(L.Text("Target and response"));
            Toggle(p,L.Text("تفعيل مساعدة التصويب"),a.Enabled,v=>a.Enabled=v);
            string[] regions={"Head","Chest","Legs"};
            Choice(p,L.Text("موضع التصويب"),new[]{L.Text("الرأس (تقديري)"),L.Text("الصدر (تقديري)"),L.Text("الأرجل (تقديري)")},Array.IndexOf(regions,a.AimPoint),i=>a.AimPoint=regions[i]);
            Slider(p,L.Text("حجم الدائرة — نصف القطر"),a.Radius,20,1200,v=>a.Radius=v,v=>v+" px (1080p)");
            Slider(p,L.Text("قوة المساعدة"),a.Strength,0,100,v=>a.Strength=v,v=>v+"%");
            Slider(p,L.Text("معامل حركة الماوس"),(int)Math.Round(a.InputScale*100),10,500,v=>a.InputScale=v/100f,v=>(v/100f).ToString("0.00", L.Culture)+"x");
            Number(p,L.Text("أقصى مسافة للمساعدة (م)"),(decimal)a.MaxDistance,10,500,v=>a.MaxDistance=(float)v,10);
            Toggle(p,L.Text("إظهار دائرة نطاق المساعدة"),a.ShowRadius,v=>a.ShowRadius=v);
            p=page.Group(L.Text("Activation mode"),1);
            string[] activation={"Scoped","Firing","Always","ScopedAndFiring","Hold"};
            Choice(p,L.Text("طريقة التفعيل"),new[]{L.Text("عند فتح السكوب فقط"),L.Text("أثناء إطلاق النار فقط"),L.Text("دائمًا داخل الدائرة"),L.Text("السكوب مع إطلاق النار"),L.Text("أثناء الضغط المستمر على زر")},Array.IndexOf(activation,a.Activation),i=>a.Activation=activation[i]);
            int[] keys={0x02,0x05,0x06,0x12};
            Choice(p,L.Text("زر التفعيل لوضع الضغط"),new[]{L.Text("زر الماوس الأيمن / RMB"),L.Text("زر جانبي / Mouse 4"),L.Text("زر جانبي / Mouse 5"),"Alt"},Array.IndexOf(keys,a.HoldKey),i=>a.HoldKey=keys[i]);
            Toggle(p,L.Text("فحص ظهور المؤشر (اختياري؛ قد يمنع الحركة في GameLoop)"),a.RequireCapturedCursor,v=>a.RequireCapturedCursor=v);
            p=page.Group(L.Text("Filters"),1);
            Toggle(p,L.Text("مساعدة على اللاعبين والبوتات (باستثناء الفريق)"),a.Players,v=>a.Players=v);
            Toggle(p,L.Text("مساعدة على أهداف التدريب"),a.Training,v=>a.Training=v);
            p.Add(SpectraFields.Note(L.Text("مواضع التصويب تقديرية من موضع الجسم وارتفاعه. عدّل المفاتيح من صفحة اختصارات الكيبورد. Insert يصغّر ويكبّر لوحة اللعبة.")));
            return page;
        }
        private Control Players()
        {
            return Category(L.Text("اللاعبون والبوتات"),options.Players,L.Text("BOT يعتمد على بيانات اللعبة. أهداف التدريب منفصلة، والحالة غير المؤكدة تُعرض UNKNOWN."),p=> {
                Toggle(p,L.Text("إظهار نوع الشخصية BOT / PLAYER"),options.BotLabels,v=>options.BotLabels=v);
                Toggle(p,L.Text("عرض البوتات"),options.ShowBots,v=>options.ShowBots=v);
                Toggle(p,L.Text("عرض اللاعبين غير المصنفين AI"),options.ShowPlayers,v=>options.ShowPlayers=v);
                Toggle(p,L.Text("عرض الحالة غير المعروفة"),options.ShowUnknownPlayers,v=>options.ShowUnknownPlayers=v);
            });
        }
        private Control Vehicles()
        {
            return Category(L.Text("المركبات"),options.Vehicles,L.Text("صحة المركبة والوقود من VehicleCommon. الوقود يُعرض كنسبة إلى سعة المركبة الفعلية."),
                p=>Toggle(p,L.Text("نسبة الوقود"),options.Fuel,v=>options.Fuel=v));
        }
        private Control Loot()
        {
            return Category(L.Text("الأسلحة والعناصر"),options.Loot,L.Text("تصفية مستقلة لتقليل ازدحام ساحة التدريب. أسماء العناصر مأخوذة من الأنواع المرصودة ومعرفاتها."),p=> {
                Toggle(p,L.Text("الأسلحة"),options.LootWeapons,v=>options.LootWeapons=v);
                Toggle(p,L.Text("الذخيرة"),options.LootAmmo,v=>options.LootAmmo=v);
                Toggle(p,L.Text("العلاج والمشروبات"),options.LootHealing,v=>options.LootHealing=v);
                Toggle(p,L.Text("الخوذات والدروع والحقائب"),options.LootArmor,v=>options.LootArmor=v);
                Toggle(p,L.Text("المناظير"),options.LootOptics,v=>options.LootOptics=v);
                Toggle(p,L.Text("المعدات والملحقات"),options.LootEquipment,v=>options.LootEquipment=v);
                Toggle(p,L.Text("أنواع أخرى"),options.LootOther,v=>options.LootOther=v);
                Toggle(p,L.Text("كمية العنصر"),options.Quantities,v=>options.Quantities=v);
                Toggle(p,L.Text("معرف العنصر"),options.ShowItemIds,v=>options.ShowItemIds=v);
            },true);
        }
        private Control Appearance()
        {
            var page=new SpectraPage(L.Text("الأداء والمظهر"),L.Text("120 Hz هو معدل طلب القراءة والرسم؛ الإطارات المرئية تعتمد أيضًا على اللعبة والشاشة. التنعيم يستخدم البيانات المقروءة فقط."));
            SpectraCard p=page.Group(L.Text("Image quality"));
            CheckBox quality=new SpectraSwitch { Text=L.InDevelopment("تحسين الصورة: NVIDIA FXAA للمحاكيات + تنعيم الأداة"),RightToLeft=L.Direction,Checked=options.ImageQualityEnabled,Height=44,AccessibleName=L.InDevelopment("تحسين الصورة: NVIDIA FXAA للمحاكيات + تنعيم الأداة"),Enabled=applyImageQuality!=null };
            Label qualityStatus=SpectraFields.Note(options.ImageQualityEnabled?L.Text("تنعيم الأداة مفعّل. إعداد FXAA محفوظ؛ تأثيره على اللعبة غير مؤكد."):L.Text("تحسين الصورة متوقف."));
            bool updating=false;
            quality.CheckedChanged+=delegate {
                if(updating||applyImageQuality==null)return;
                try { qualityStatus.Text=applyImageQuality(quality.Checked); }
                catch(Exception ex) { updating=true;quality.Checked=options.ImageQualityEnabled;updating=false;qualityStatus.Text=L.Format("تعذر التطبيق؛ لم يتغير الاختيار. {0}", ex.Message); }
            };
            p.Add(quality);p.Add(qualityStatus);
            p.Add(SpectraFields.Note(L.Text("FXAA يُطلب من تعريف NVIDIA لـ GameLoop وLDPlayer. أعد تشغيل المحاكي بعد التفعيل أو الإيقاف؛ الاستجابة تعتمد على دعمه وطريقة الرسم، ولا يمكن تأكيدها من هنا. تنعيم الأداة يطبق فورًا.\nDLSS ورفع الدقة: غير متاحين هنا؛ يحتاج DLSS دعم محرك اللعبة، وNVIDIA Image Scaling يحتاج إعدادات العرض المناسبة. لا يضمن هذا الخيار زيادة FPS أو أفضل نتيجة لكل لعبة.")));
            p=page.Group(L.Text("In-game panel"));
            Toggle(p,L.Text("Show in-game panel by default"),options.QuickHudVisible,v=>options.QuickHudVisible=v);
            p=page.Group(L.Text("Rendering"),1);
            Number(p,L.Text("معدل الرسم المطلوب (Hz)"),options.RefreshRate,30,120,v=>options.RefreshRate=(int)v,30);
            Number(p,L.Text("حجم الخط"),(decimal)options.FontSize,7,18,v=>options.FontSize=(float)v,.5m); Number(p,L.Text("سُمك الخطوط والمربعات"),(decimal)options.LineWidth,1,4,v=>options.LineWidth=(float)v,.5m); Number(p,L.Text("أقصى عدد للتسميات على الشاشة"),options.LabelLimit,10,200,v=>options.LabelLimit=(int)v);
            Number(p,L.Text("حد عام للمسافة (م)"),(decimal)options.MaxDistance,10,5000,v=>options.MaxDistance=(float)v);
            return page;
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
