using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ESPWorkbench;

internal static class UiContractTests
{
    private static int count;
    private static void Check(bool condition,string name) { if(!condition)throw new Exception(name);count++;Console.WriteLine("PASS "+name); }
    private static IEnumerable<Control> Children(Control parent) { foreach(Control child in parent.Controls) { yield return child;foreach(Control nested in Children(child))yield return nested; } }
    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            RenderOptions options=new RenderOptions();int changes=0;
            using(OptionsForm factory=new OptionsForm(options,delegate {},null,delegate { changes++; }))
            {
                using(Control page=factory.CreateEmbeddedPage(0))
                {
                    Check(!options.Visibility.Enabled,"visibility coloring preserves the existing default");
                    int colorChoices=0;CheckBox visibility=null;
                    foreach(Control child in Children(page))
                    {
                        if(child is Button&&child.AccessibleName!=null&&child.AccessibleName.StartsWith("لون "))colorChoices++;
                        if(child is CheckBox&&child.Text=="تغيير ألوان اللاعبين حسب حالة الرؤية")visibility=(CheckBox)child;
                    }
                    Check(colorChoices==3,"ESP exposes independent visible, occluded, and unknown color pickers");
                    Check(visibility!=null,"ESP exposes the visibility color enable switch");
                    visibility.Checked=true;Check(options.Visibility.Enabled&&changes==1,"visibility switch updates the shared option and autosave observer");
                    foreach(int width in new[]{1100,650})
                    {
                        page.Size=new Size(width,850);page.PerformLayout();bool fits=true;
                        foreach(Control child in page.Controls)
                        {
                            FlowLayoutPanel row=child as FlowLayoutPanel;if(row==null||row.Controls.Count!=2||!(row.Controls[1] is Button))continue;
                            row.PerformLayout();fits&=row.Controls[0].Right<=row.Controls[1].Left&&row.Controls[1].Right<=row.ClientSize.Width;
                        }
                        Check(fits,"color picker rows fit the content width "+width);
                    }
                }
                using(Control page=factory.CreateEmbeddedPage(1))
                {
                    Control mode=null;foreach(Control child in Children(page))if(child.AccessibleName=="التعامل مع الحواجز")mode=child;
                    Check(mode!=null,"Aim exposes an obstacle handling selector");
                    System.Reflection.PropertyInfo selection=mode.GetType().GetProperty("SelectedIndex");
                    Check((int)selection.GetValue(mode,null)==0&&!options.Aim.VisibleOnly,"Aim defaults to the existing unrestricted selection mode");
                    int before=changes;selection.SetValue(mode,1,null);
                    Check(options.Aim.VisibleOnly&&changes==before+1,"visible-only selection updates the shared option and autosave observer");
                    selection.SetValue(mode,0,null);Check(!options.Aim.VisibleOnly,"normal aim selection can be restored from the same selector");
                }
            }
            Console.WriteLine(count+" visibility UI checks passed. No windows displayed.");return 0;
        }
        catch(Exception error) { Console.WriteLine("FAILED "+error);return 1; }
    }
}
