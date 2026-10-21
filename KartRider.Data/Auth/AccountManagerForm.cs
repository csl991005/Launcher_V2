using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KartRider;

/// <summary>
/// 账号管理窗口：认证开关、账号增删、口令重置、昵称绑定、启用/禁用。
/// 直接读写 Profile/AuthServer.json，与命令行 --auth 操作的是同一份数据。
/// </summary>
internal sealed class AccountManagerForm : Form
{
    private readonly ListView _list;
    private readonly CheckBox _enableCheck;
    private readonly Label _statusLabel;
    private readonly Label _pathLabel;
    private readonly Button _addButton;
    private readonly Button _resetButton;
    private readonly Button _accountEnableButton;
    private readonly Button _deleteButton;
    private readonly Button _refreshButton;
    private readonly Button _closeButton;

    private AuthServerConfig _config;
    private bool _suppressEvents;

    public AccountManagerForm()
    {
        _list = new ListView();
        _enableCheck = new CheckBox();
        _statusLabel = new Label();
        _pathLabel = new Label();
        _addButton = new Button();
        _resetButton = new Button();
        _accountEnableButton = new Button();
        _deleteButton = new Button();
        _refreshButton = new Button();
        _closeButton = new Button();

        InitializeComponent();
        ReloadConfig();
    }

    private void InitializeComponent()
    {
        ColumnHeader accountColumn = new ColumnHeader();
        ColumnHeader nicknameColumn = new ColumnHeader();
        ColumnHeader stateColumn = new ColumnHeader();
        ColumnHeader countColumn = new ColumnHeader();
        ColumnHeader lastColumn = new ColumnHeader();
        ColumnHeader noteColumn = new ColumnHeader();
        SuspendLayout();

        _enableCheck.AutoSize = true;
        _enableCheck.Location = new Point(16, 10);
        _enableCheck.Font = new Font("宋体", 10F, FontStyle.Bold, GraphicsUnit.Point, 134);
        _enableCheck.Text = "启用账号认证";
        _enableCheck.CheckedChanged += EnableCheck_CheckedChanged;

        _statusLabel.AutoSize = false;
        _statusLabel.Location = new Point(160, 12);
        _statusLabel.Size = new Size(480, 20);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Text = "读取中...";

        _pathLabel.AutoSize = false;
        _pathLabel.Location = new Point(16, 40);
        _pathLabel.Size = new Size(624, 18);
        _pathLabel.ForeColor = Color.Gray;
        _pathLabel.AutoEllipsis = true;
        _pathLabel.Text = "配置文件：" + AuthStore.ServerConfigPath;

        accountColumn.Text = "账号";
        accountColumn.Width = 120;
        nicknameColumn.Text = "游戏昵称";
        nicknameColumn.Width = 120;
        stateColumn.Text = "状态";
        stateColumn.Width = 60;
        countColumn.Text = "登录次数";
        countColumn.Width = 70;
        lastColumn.Text = "最后登录";
        lastColumn.Width = 135;
        noteColumn.Text = "备注";
        noteColumn.Width = 115;

        _list.Location = new Point(16, 64);
        _list.Size = new Size(624, 310);
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.GridLines = true;
        _list.MultiSelect = false;
        _list.HideSelection = false;
        _list.Columns.AddRange(new ColumnHeader[]
        {
            accountColumn, nicknameColumn, stateColumn, countColumn, lastColumn, noteColumn,
        });
        _list.SelectedIndexChanged += (s, e) => UpdateButtonState();
        _list.DoubleClick += (s, e) => ResetPassword();

        _addButton.Location = new Point(16, 386);
        _addButton.Size = new Size(90, 28);
        _addButton.Text = "新增账号";
        _addButton.UseVisualStyleBackColor = true;
        _addButton.Click += AddButton_Click;

        _resetButton.Location = new Point(112, 386);
        _resetButton.Size = new Size(90, 28);
        _resetButton.Text = "重置口令";
        _resetButton.UseVisualStyleBackColor = true;
        _resetButton.Click += (s, e) => ResetPassword();

        _accountEnableButton.Location = new Point(208, 386);
        _accountEnableButton.Size = new Size(90, 28);
        _accountEnableButton.Text = "禁用账号";
        _accountEnableButton.UseVisualStyleBackColor = true;
        _accountEnableButton.Click += AccountEnableButton_Click;

        _deleteButton.Location = new Point(304, 386);
        _deleteButton.Size = new Size(90, 28);
        _deleteButton.Text = "删除账号";
        _deleteButton.UseVisualStyleBackColor = true;
        _deleteButton.Click += DeleteButton_Click;

        _refreshButton.Location = new Point(456, 386);
        _refreshButton.Size = new Size(90, 28);
        _refreshButton.Text = "刷新";
        _refreshButton.UseVisualStyleBackColor = true;
        _refreshButton.Click += (s, e) => ReloadConfig();

        _closeButton.Location = new Point(550, 386);
        _closeButton.Size = new Size(90, 28);
        _closeButton.Text = "关闭";
        _closeButton.UseVisualStyleBackColor = true;
        _closeButton.DialogResult = DialogResult.Cancel;

        AutoScaleDimensions = new SizeF(6F, 12F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(656, 428);
        Font = new Font("宋体", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "账号管理";
        CancelButton = _closeButton;

        Controls.Add(_enableCheck);
        Controls.Add(_statusLabel);
        Controls.Add(_pathLabel);
        Controls.Add(_list);
        Controls.Add(_addButton);
        Controls.Add(_resetButton);
        Controls.Add(_accountEnableButton);
        Controls.Add(_deleteButton);
        Controls.Add(_refreshButton);
        Controls.Add(_closeButton);

        ResumeLayout(false);
        PerformLayout();
    }

    // ---------------------------------------------------------------- 数据

    private void ReloadConfig()
    {
        try
        {
            _config = AuthStore.GetServerConfig();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "读取配置文件失败：" + ex.Message, "账号管理", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _config = new AuthServerConfig();
        }

        RefreshList();
    }

    private void RefreshList()
    {
        List<AuthAccount> accounts = GetAccounts();
        string selected = GetSelectedAccountName();

        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (AuthAccount account in accounts)
        {
            ListViewItem item = new ListViewItem(account.Account ?? string.Empty);
            item.SubItems.Add(string.IsNullOrEmpty(account.Nickname) ? "（由玩家自定）" : account.Nickname);
            item.SubItems.Add(account.Enabled ? "启用" : "禁用");
            item.SubItems.Add(account.LoginCount.ToString());
            item.SubItems.Add(string.IsNullOrEmpty(account.LastLoginAt) ? "-" : account.LastLoginAt);
            item.SubItems.Add(account.Note ?? string.Empty);
            item.Tag = account.Account;

            if (!account.Enabled)
                item.ForeColor = Color.Gray;
            else if (account.DefaultPassword)
                item.ForeColor = Color.FromArgb(190, 90, 0);

            _list.Items.Add(item);

            if (!string.IsNullOrEmpty(selected) &&
                string.Equals(account.Account, selected, StringComparison.OrdinalIgnoreCase))
            {
                item.Selected = true;
            }
        }

        _list.EndUpdate();

        UpdateStatus(accounts);
        UpdateButtonState();
    }

    private List<AuthAccount> GetAccounts()
    {
        if (_config == null || _config.Accounts == null)
            return new List<AuthAccount>();

        return _config.Accounts
            .Where(a => a != null)
            .OrderBy(a => a.Account ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void UpdateStatus(List<AuthAccount> accounts)
    {
        int enabledCount = accounts.Count(a => a.Enabled);
        bool hasDefaultPassword = accounts.Any(a => a.Enabled && a.DefaultPassword);

        _suppressEvents = true;
        _enableCheck.Checked = _config.Enabled;
        _suppressEvents = false;

        if (_config.Enabled)
        {
            _statusLabel.Text = "已启用 · 共 " + accounts.Count + " 个账号，可用 " + enabledCount + " 个" +
                                (hasDefaultPassword ? " · 存在未修改的初始口令" : string.Empty);
            _statusLabel.ForeColor = hasDefaultPassword
                ? Color.FromArgb(190, 90, 0)
                : Color.FromArgb(0, 120, 60);
        }
        else
        {
            _statusLabel.Text = "未启用 · 任何账号密码都能进入游戏";
            _statusLabel.ForeColor = Color.FromArgb(190, 0, 0);
        }
    }

    private void UpdateButtonState()
    {
        AuthAccount selected = GetSelectedAccount();
        bool has = selected != null;

        _resetButton.Enabled = has;
        _deleteButton.Enabled = has;
        _accountEnableButton.Enabled = has;
        _accountEnableButton.Text = has && selected.Enabled ? "禁用账号" : "启用账号";
    }

    private AuthAccount GetSelectedAccount()
    {
        if (_list.SelectedItems.Count == 0 || _config == null || _config.Accounts == null)
            return null;

        string name = _list.SelectedItems[0].Tag as string;
        if (string.IsNullOrEmpty(name))
            return null;

        return _config.Accounts.FirstOrDefault(a =>
            a != null && string.Equals(a.Account, name, StringComparison.OrdinalIgnoreCase));
    }

    private string GetSelectedAccountName()
    {
        return _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as string;
    }

    // ---------------------------------------------------------------- 操作

    private void EnableCheck_CheckedChanged(object sender, EventArgs e)
    {
        if (_suppressEvents)
            return;

        bool target = _enableCheck.Checked;

        if (target)
        {
            int enabledCount = GetAccounts().Count(a => a.Enabled);
            if (enabledCount == 0)
            {
                MessageBox.Show(this,
                    "还没有任何可用账号，启用后所有玩家都会被拒绝。\n\n请先点“新增账号”创建至少一个账号。",
                    "无法启用",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                RevertCheck();
                return;
            }

            DialogResult result = MessageBox.Show(this,
                "启用后，没有账号的玩家将无法进入游戏。\n\n确认启用账号认证？",
                "启用认证",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                RevertCheck();
                return;
            }
        }
        else
        {
            DialogResult result = MessageBox.Show(this,
                "关闭后，任何知道服务器 IP 和端口的人都能直接进入游戏。\n\n确认关闭账号认证？",
                "关闭认证",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
            {
                RevertCheck();
                return;
            }
        }

        _config.Enabled = target;

        if (SaveConfig())
            ReloadConfig();
        else
            RevertCheck();
    }

    private void RevertCheck()
    {
        _suppressEvents = true;
        _enableCheck.Checked = !_enableCheck.Checked;
        _suppressEvents = false;
    }

    private void AddButton_Click(object sender, EventArgs e)
    {
        using (AccountEditForm form = new AccountEditForm(null))
        {
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            string accountName = form.AccountName;

            if (_config.Accounts.Any(a =>
                a != null && string.Equals(a.Account, accountName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, "账号 " + accountName + " 已存在，请使用“重置口令”。", "账号管理",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AuthAccount account = new AuthAccount
            {
                Account = accountName,
                Nickname = form.Nickname,
                Note = form.Note,
                Enabled = true,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };

            AuthStore.ApplyPassword(account, form.Password);
            _config.Accounts.Add(account);

            if (!SaveConfig())
                return;

            ReloadConfig();

            using (PasswordResultForm result = new PasswordResultForm(accountName, form.Password, false))
                result.ShowDialog(this);

            AskEnableIfNeeded();
        }
    }

    /// <summary>新增账号后，如果认证还关着，提醒一次，避免"以为开了其实没开"。</summary>
    private void AskEnableIfNeeded()
    {
        if (_config.Enabled)
            return;

        DialogResult result = MessageBox.Show(this,
            "账号已创建。\n\n当前账号认证仍是关闭状态，任何账号密码都可以进入。\n是否现在启用？",
            "启用认证",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        _config.Enabled = true;

        if (SaveConfig())
            ReloadConfig();
    }

    private void ResetPassword()
    {
        AuthAccount selected = GetSelectedAccount();
        if (selected == null)
            return;

        using (AccountEditForm form = new AccountEditForm(selected))
        {
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            AuthStore.ApplyPassword(selected, form.Password);
            selected.Nickname = form.Nickname;
            selected.Note = form.Note;
            selected.DefaultPassword = false;

            if (!SaveConfig())
                return;

            ReloadConfig();

            using (PasswordResultForm result = new PasswordResultForm(selected.Account, form.Password, true))
                result.ShowDialog(this);
        }
    }

    private void AccountEnableButton_Click(object sender, EventArgs e)
    {
        AuthAccount selected = GetSelectedAccount();
        if (selected == null)
            return;

        selected.Enabled = !selected.Enabled;

        if (SaveConfig())
            ReloadConfig();
    }

    private void DeleteButton_Click(object sender, EventArgs e)
    {
        AuthAccount selected = GetSelectedAccount();
        if (selected == null)
            return;

        int remaining = GetAccounts().Count(a => a.Enabled && a != selected);

        if (_config.Enabled && remaining == 0)
        {
            MessageBox.Show(this,
                "这是最后一个可用账号，删除后没有玩家能登录。\n\n请先新增其它账号，或关闭账号认证。",
                "无法删除",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        DialogResult result = MessageBox.Show(this,
            "确认删除账号 " + selected.Account + " ？\n\n该玩家的 Profile 存档不会被删除。",
            "删除账号",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        _config.Accounts.Remove(selected);

        if (SaveConfig())
            ReloadConfig();
    }

    private bool SaveConfig()
    {
        try
        {
            AuthStore.SaveServerConfig(_config);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存配置失败：" + ex.Message, "账号管理", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}
