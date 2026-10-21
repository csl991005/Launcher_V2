using System;
using System.Drawing;
using System.Windows.Forms;

namespace KartRider;

/// <summary>
/// 口令展示窗口：口令只在这里显示一次，方便复制发给玩家。
/// </summary>
internal sealed class PasswordResultForm : Form
{
    private readonly string _password;

    public PasswordResultForm(string account, string password, bool isReset)
    {
        _password = password;

        Label titleLabel = new Label();
        TextBox passwordBox = new TextBox();
        Button copyButton = new Button();
        Button closeButton = new Button();
        Label hintLabel = new Label();

        SuspendLayout();

        titleLabel.AutoSize = true;
        titleLabel.Location = new Point(16, 18);
        titleLabel.Text = (isReset ? "已重置口令：" : "已创建账号：") + account;

        passwordBox.Location = new Point(16, 48);
        passwordBox.Size = new Size(392, 25);
        passwordBox.ReadOnly = true;
        passwordBox.Font = new Font("Consolas", 11F, FontStyle.Regular, GraphicsUnit.Point, 0);
        passwordBox.Text = password;
        passwordBox.SelectAll();

        hintLabel.AutoSize = true;
        hintLabel.ForeColor = Color.Gray;
        hintLabel.Location = new Point(16, 82);
        hintLabel.Text = "口令只显示这一次，请复制后发给玩家；忘记可在账号管理中重置。";

        copyButton.Location = new Point(232, 112);
        copyButton.Size = new Size(80, 28);
        copyButton.Text = "复制";
        copyButton.UseVisualStyleBackColor = true;
        copyButton.Click += CopyButton_Click;

        closeButton.Location = new Point(328, 112);
        closeButton.Size = new Size(80, 28);
        closeButton.Text = "关闭";
        closeButton.UseVisualStyleBackColor = true;
        closeButton.DialogResult = DialogResult.OK;

        AutoScaleDimensions = new SizeF(6F, 12F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(424, 154);
        Font = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "账号口令";

        Controls.Add(titleLabel);
        Controls.Add(passwordBox);
        Controls.Add(hintLabel);
        Controls.Add(copyButton);
        Controls.Add(closeButton);

        AcceptButton = closeButton;
        CancelButton = closeButton;

        ResumeLayout(false);
        PerformLayout();
    }

    private void CopyButton_Click(object sender, EventArgs e)
    {
        try
        {
            Clipboard.SetText(_password);
            MessageBox.Show(this, "已复制到剪贴板。", "账号口令", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "复制失败：" + ex.Message, "账号口令", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
