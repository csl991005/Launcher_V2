using System;
using System.Drawing;
using System.Windows.Forms;

namespace KartRider;

/// <summary>
/// 在主窗口上挂一个“账号管理”按钮。
///
/// 通过 Application.Idle 等待主窗口就绪后动态添加控件，
/// 这样不需要修改 Launcher.Designer.cs / Launcher.cs，上游同步不会冲突。
/// </summary>
internal static class AuthUiHook
{
    private static bool _installed;
    private static bool _attached;

    public static void Install()
    {
        if (_installed)
            return;

        _installed = true;

        try
        {
            Application.Idle += OnIdle;
        }
        catch (Exception ex)
        {
            Log("[Auth] 安装界面钩子失败: " + ex.Message);
        }
    }

    private static void OnIdle(object sender, EventArgs e)
    {
        if (_attached)
            return;

        Launcher form = Program.LauncherDlg;
        if (form == null || form.IsDisposed || !form.IsHandleCreated)
            return;

        _attached = true;

        try
        {
            Application.Idle -= OnIdle;
        }
        catch
        {
        }

        try
        {
            Attach(form);
        }
        catch (Exception ex)
        {
            Log("[Auth] 添加账号管理入口失败: " + ex.Message);
        }
    }

    private static void Attach(Launcher form)
    {
        if (form.Controls["AuthAccountManagerButton"] != null)
            return;

        Control anchor = form.Controls["Setting_Button"];
        Control row = form.Controls["button_ToggleTerminal"];

        Button button = new Button();
        button.Name = "AuthAccountManagerButton";
        button.Text = "账号管理";
        button.UseVisualStyleBackColor = true;

        if (anchor != null && row != null)
        {
            // 复用已有控件的尺寸与字体，保证高 DPI 下与其它按钮一致
            button.Font = anchor.Font;
            button.Size = anchor.Size;
            button.Location = new Point(anchor.Left, row.Top + row.Height + 6);
        }
        else
        {
            button.Font = form.Font;
            button.Size = new Size(114, 23);
            button.Location = new Point(19, 107);
        }

        button.Click += Button_Click;

        form.Controls.Add(button);
        button.BringToFront();

        Log("[Auth] 已在主界面添加“账号管理”按钮");
    }

    private static void Button_Click(object sender, EventArgs e)
    {
        try
        {
            using (AccountManagerForm manager = new AccountManagerForm())
            {
                manager.ShowDialog(Program.LauncherDlg);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "打开账号管理失败：" + ex.Message,
                "账号管理",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void Log(string message)
    {
        try
        {
            Console.WriteLine(message);
        }
        catch
        {
        }
    }
}
