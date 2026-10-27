namespace CollationModifier
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.statusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.progressBar = new System.Windows.Forms.ToolStripProgressBar();
            this.panelAuth = new System.Windows.Forms.TableLayoutPanel();
            this.btnConnect = new System.Windows.Forms.Button();
            this.txtPassword = new System.Windows.Forms.TextBox();
            this.lblPassword = new System.Windows.Forms.Label();
            this.txtUsername = new System.Windows.Forms.TextBox();
            this.lblUsername = new System.Windows.Forms.Label();
            this.cmbAuthType = new System.Windows.Forms.ComboBox();
            this.lblAuthType = new System.Windows.Forms.Label();
            this.txtServerName = new System.Windows.Forms.TextBox();
            this.lblServerName = new System.Windows.Forms.Label();
            this.lblAuthHeader = new System.Windows.Forms.Label();
            this.panelDashboard = new System.Windows.Forms.Panel();
            this.lblDisconnectedOverlay = new System.Windows.Forms.Label();
            this.panelDashboardContent = new System.Windows.Forms.TableLayoutPanel();
            this.lblWarning = new System.Windows.Forms.Label();
            this.btnProcess = new System.Windows.Forms.Button();
            this.tblTargetConfigLayout = new System.Windows.Forms.TableLayoutPanel();
            this.txtBackupDirectory = new System.Windows.Forms.TextBox();
            this.lblBackupDir = new System.Windows.Forms.Label();
            this.txtTargetCollation = new System.Windows.Forms.TextBox();
            this.lblTargetCollation = new System.Windows.Forms.Label();
            this.lblTargetConfigHeader = new System.Windows.Forms.Label();
            this.panelMetadata = new System.Windows.Forms.GroupBox();
            this.tblMetadataLayout = new System.Windows.Forms.TableLayoutPanel();
            this.lblSizeValue = new System.Windows.Forms.Label();
            this.lblOwnerValue = new System.Windows.Forms.Label();
            this.lblCompatLevelValue = new System.Windows.Forms.Label();
            this.lblCollationValue = new System.Windows.Forms.Label();
            this.lblSizeTitle = new System.Windows.Forms.Label();
            this.lblOwnerTitle = new System.Windows.Forms.Label();
            this.lblCompatLevelTitle = new System.Windows.Forms.Label();
            this.lblCollationTitle = new System.Windows.Forms.Label();
            this.cmbDatabases = new System.Windows.Forms.ComboBox();
            this.lblSelectDb = new System.Windows.Forms.Label();
            this.lblDashboardHeader = new System.Windows.Forms.Label();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.statusStrip.SuspendLayout();
            this.panelAuth.SuspendLayout();
            this.panelDashboard.SuspendLayout();
            this.panelDashboardContent.SuspendLayout();
            this.panelMetadata.SuspendLayout();
            this.tblMetadataLayout.SuspendLayout();
            this.tblTargetConfigLayout.SuspendLayout();
            this.SuspendLayout();
            // 
            // statusStrip
            // 
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.statusLabel,
            this.progressBar});
            this.statusStrip.Location = new System.Drawing.Point(0, 604);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(900, 22);
            this.statusStrip.TabIndex = 0;
            this.statusStrip.Text = "statusStrip";
            // 
            // statusLabel
            // 
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(783, 17);
            this.statusLabel.Spring = true;
            this.statusLabel.Text = "Ready";
            this.statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // progressBar
            // 
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(100, 16);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressBar.Visible = false;
            // 
            // panelAuth
            // 
            this.panelAuth.AutoScroll = true;
            this.panelAuth.AutoSize = true;
            this.panelAuth.BackColor = System.Drawing.Color.White;
            this.panelAuth.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panelAuth.ColumnCount = 1;
            this.panelAuth.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.panelAuth.Controls.Add(this.lblAuthHeader, 0, 0);
            this.panelAuth.Controls.Add(this.lblServerName, 0, 1);
            this.panelAuth.Controls.Add(this.txtServerName, 0, 2);
            this.panelAuth.Controls.Add(this.lblAuthType, 0, 3);
            this.panelAuth.Controls.Add(this.cmbAuthType, 0, 4);
            this.panelAuth.Controls.Add(this.lblUsername, 0, 5);
            this.panelAuth.Controls.Add(this.txtUsername, 0, 6);
            this.panelAuth.Controls.Add(this.lblPassword, 0, 7);
            this.panelAuth.Controls.Add(this.txtPassword, 0, 8);
            this.panelAuth.Controls.Add(this.btnConnect, 0, 9);
            this.panelAuth.Dock = System.Windows.Forms.DockStyle.Left;
            this.panelAuth.Location = new System.Drawing.Point(0, 0);
            this.panelAuth.Name = "panelAuth";
            this.panelAuth.Padding = new System.Windows.Forms.Padding(20);
            this.panelAuth.RowCount = 10;
            this.panelAuth.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelAuth.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelAuth.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelAuth.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelAuth.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelAuth.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelAuth.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelAuth.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelAuth.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelAuth.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelAuth.Size = new System.Drawing.Size(320, 604);
            this.panelAuth.TabIndex = 1;
            // 
            // lblAuthHeader
            // 
            this.lblAuthHeader.AutoSize = true;
            this.lblAuthHeader.Font = new System.Drawing.Font("Segoe UI", 14.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblAuthHeader.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(41)))), ((int)(((byte)(59)))));
            this.lblAuthHeader.Location = new System.Drawing.Point(20, 20);
            this.lblAuthHeader.Margin = new System.Windows.Forms.Padding(0, 0, 0, 12);
            this.lblAuthHeader.Name = "lblAuthHeader";
            this.lblAuthHeader.Size = new System.Drawing.Size(144, 25);
            this.lblAuthHeader.TabIndex = 0;
            this.lblAuthHeader.Text = "Authentication";
            // 
            // lblServerName
            // 
            this.lblServerName.AutoSize = true;
            this.lblServerName.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblServerName.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.lblServerName.Location = new System.Drawing.Point(20, 57);
            this.lblServerName.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this.lblServerName.Name = "lblServerName";
            this.lblServerName.Size = new System.Drawing.Size(72, 15);
            this.lblServerName.TabIndex = 1;
            this.lblServerName.Text = "Server Name";
            // 
            // txtServerName
            // 
            this.txtServerName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtServerName.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtServerName.Location = new System.Drawing.Point(20, 76);
            this.txtServerName.Margin = new System.Windows.Forms.Padding(0, 0, 0, 12);
            this.txtServerName.Name = "txtServerName";
            this.txtServerName.Size = new System.Drawing.Size(278, 25);
            this.txtServerName.TabIndex = 2;
            this.txtServerName.Text = "localhost";
            // 
            // lblAuthType
            // 
            this.lblAuthType.AutoSize = true;
            this.lblAuthType.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblAuthType.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.lblAuthType.Location = new System.Drawing.Point(20, 113);
            this.lblAuthType.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this.lblAuthType.Name = "lblAuthType";
            this.lblAuthType.Size = new System.Drawing.Size(114, 15);
            this.lblAuthType.TabIndex = 3;
            this.lblAuthType.Text = "Authentication Type";
            // 
            // cmbAuthType
            // 
            this.cmbAuthType.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cmbAuthType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbAuthType.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.cmbAuthType.FormattingEnabled = true;
            this.cmbAuthType.Location = new System.Drawing.Point(20, 132);
            this.cmbAuthType.Margin = new System.Windows.Forms.Padding(0, 0, 0, 12);
            this.cmbAuthType.Name = "cmbAuthType";
            this.cmbAuthType.Size = new System.Drawing.Size(278, 25);
            this.cmbAuthType.TabIndex = 4;
            this.cmbAuthType.SelectedIndexChanged += new System.EventHandler(this.cmbAuthType_SelectedIndexChanged);
            // 
            // lblUsername
            // 
            this.lblUsername.AutoSize = true;
            this.lblUsername.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblUsername.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.lblUsername.Location = new System.Drawing.Point(20, 169);
            this.lblUsername.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this.lblUsername.Name = "lblUsername";
            this.lblUsername.Size = new System.Drawing.Size(60, 15);
            this.lblUsername.TabIndex = 5;
            this.lblUsername.Text = "Username";
            // 
            // txtUsername
            // 
            this.txtUsername.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtUsername.Enabled = false;
            this.txtUsername.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtUsername.Location = new System.Drawing.Point(20, 188);
            this.txtUsername.Margin = new System.Windows.Forms.Padding(0, 0, 0, 12);
            this.txtUsername.Name = "txtUsername";
            this.txtUsername.Size = new System.Drawing.Size(278, 25);
            this.txtUsername.TabIndex = 6;
            // 
            // lblPassword
            // 
            this.lblPassword.AutoSize = true;
            this.lblPassword.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblPassword.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.lblPassword.Location = new System.Drawing.Point(20, 225);
            this.lblPassword.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this.lblPassword.Name = "lblPassword";
            this.lblPassword.Size = new System.Drawing.Size(57, 15);
            this.lblPassword.TabIndex = 7;
            this.lblPassword.Text = "Password";
            // 
            // txtPassword
            // 
            this.txtPassword.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtPassword.Enabled = false;
            this.txtPassword.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtPassword.Location = new System.Drawing.Point(20, 244);
            this.txtPassword.Margin = new System.Windows.Forms.Padding(0, 0, 0, 16);
            this.txtPassword.Name = "txtPassword";
            this.txtPassword.Size = new System.Drawing.Size(278, 25);
            this.txtPassword.TabIndex = 8;
            this.txtPassword.UseSystemPasswordChar = true;
            // 
            // btnConnect
            // 
            this.btnConnect.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.btnConnect.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(59)))), ((int)(((byte)(130)))), ((int)(((byte)(246)))));
            this.btnConnect.FlatAppearance.BorderSize = 0;
            this.btnConnect.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnConnect.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnConnect.ForeColor = System.Drawing.Color.White;
            this.btnConnect.Location = new System.Drawing.Point(20, 285);
            this.btnConnect.Margin = new System.Windows.Forms.Padding(0, 8, 0, 0);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(278, 36);
            this.btnConnect.TabIndex = 9;
            this.btnConnect.Text = "Connect";
            this.btnConnect.UseVisualStyleBackColor = false;
            this.btnConnect.Click += new System.EventHandler(this.btnConnect_Click);
            // 
            // panelDashboard
            // 
            this.panelDashboard.AutoScroll = true;
            this.panelDashboard.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(248)))), ((int)(((byte)(250)))), ((int)(((byte)(252)))));
            this.panelDashboard.Controls.Add(this.lblDisconnectedOverlay);
            this.panelDashboard.Controls.Add(this.panelDashboardContent);
            this.panelDashboard.Controls.Add(this.lblDashboardHeader);
            this.panelDashboard.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelDashboard.Location = new System.Drawing.Point(320, 0);
            this.panelDashboard.Name = "panelDashboard";
            this.panelDashboard.Padding = new System.Windows.Forms.Padding(30);
            this.panelDashboard.Size = new System.Drawing.Size(580, 604);
            this.panelDashboard.TabIndex = 2;
            // 
            // lblDisconnectedOverlay
            // 
            this.lblDisconnectedOverlay.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblDisconnectedOverlay.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(100)))), ((int)(((byte)(116)))), ((int)(((byte)(139)))));
            this.lblDisconnectedOverlay.Location = new System.Drawing.Point(33, 270);
            this.lblDisconnectedOverlay.Name = "lblDisconnectedOverlay";
            this.lblDisconnectedOverlay.Size = new System.Drawing.Size(517, 30);
            this.lblDisconnectedOverlay.TabIndex = 2;
            this.lblDisconnectedOverlay.Text = "Please connect to a server to continue.";
            this.lblDisconnectedOverlay.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // panelDashboardContent
            // 
            this.panelDashboardContent.AutoScroll = true;
            this.panelDashboardContent.AutoSize = true;
            this.panelDashboardContent.ColumnCount = 1;
            this.panelDashboardContent.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.panelDashboardContent.Controls.Add(this.lblSelectDb, 0, 0);
            this.panelDashboardContent.Controls.Add(this.cmbDatabases, 0, 1);
            this.panelDashboardContent.Controls.Add(this.panelMetadata, 0, 2);
            this.panelDashboardContent.Controls.Add(this.lblTargetConfigHeader, 0, 3);
            this.panelDashboardContent.Controls.Add(this.tblTargetConfigLayout, 0, 4);
            this.panelDashboardContent.Controls.Add(this.btnProcess, 0, 5);
            this.panelDashboardContent.Controls.Add(this.lblWarning, 0, 6);
            this.panelDashboardContent.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelDashboardContent.Location = new System.Drawing.Point(30, 62);
            this.panelDashboardContent.Name = "panelDashboardContent";
            this.panelDashboardContent.RowCount = 7;
            this.panelDashboardContent.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelDashboardContent.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelDashboardContent.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelDashboardContent.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelDashboardContent.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelDashboardContent.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelDashboardContent.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.panelDashboardContent.Size = new System.Drawing.Size(520, 512);
            this.panelDashboardContent.TabIndex = 1;
            // 
            // lblSelectDb
            // 
            this.lblSelectDb.AutoSize = true;
            this.lblSelectDb.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblSelectDb.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.lblSelectDb.Location = new System.Drawing.Point(0, 4);
            this.lblSelectDb.Margin = new System.Windows.Forms.Padding(0, 4, 0, 4);
            this.lblSelectDb.Name = "lblSelectDb";
            this.lblSelectDb.Size = new System.Drawing.Size(105, 17);
            this.lblSelectDb.TabIndex = 0;
            this.lblSelectDb.Text = "Select Database";
            // 
            // cmbDatabases
            // 
            this.cmbDatabases.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.cmbDatabases.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbDatabases.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.cmbDatabases.FormattingEnabled = true;
            this.cmbDatabases.Location = new System.Drawing.Point(0, 25);
            this.cmbDatabases.Margin = new System.Windows.Forms.Padding(0, 0, 0, 16);
            this.cmbDatabases.Name = "cmbDatabases";
            this.cmbDatabases.Size = new System.Drawing.Size(520, 25);
            this.cmbDatabases.TabIndex = 1;
            this.cmbDatabases.SelectedIndexChanged += new System.EventHandler(this.cmbDatabases_SelectedIndexChanged);
            // 
            // panelMetadata
            // 
            this.panelMetadata.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panelMetadata.AutoSize = true;
            this.panelMetadata.BackColor = System.Drawing.Color.White;
            this.panelMetadata.Controls.Add(this.tblMetadataLayout);
            this.panelMetadata.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.panelMetadata.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(41)))), ((int)(((byte)(59)))));
            this.panelMetadata.Location = new System.Drawing.Point(0, 66);
            this.panelMetadata.Margin = new System.Windows.Forms.Padding(0, 0, 0, 16);
            this.panelMetadata.Name = "panelMetadata";
            this.panelMetadata.Padding = new System.Windows.Forms.Padding(10);
            this.panelMetadata.Size = new System.Drawing.Size(520, 150);
            this.panelMetadata.TabIndex = 2;
            this.panelMetadata.TabStop = false;
            this.panelMetadata.Text = "Metadata";
            // 
            // tblMetadataLayout
            // 
            this.tblMetadataLayout.AutoSize = true;
            this.tblMetadataLayout.ColumnCount = 2;
            this.tblMetadataLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.tblMetadataLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tblMetadataLayout.Controls.Add(this.lblCollationTitle, 0, 0);
            this.tblMetadataLayout.Controls.Add(this.lblCollationValue, 1, 0);
            this.tblMetadataLayout.Controls.Add(this.lblCompatLevelTitle, 0, 1);
            this.tblMetadataLayout.Controls.Add(this.lblCompatLevelValue, 1, 1);
            this.tblMetadataLayout.Controls.Add(this.lblOwnerTitle, 0, 2);
            this.tblMetadataLayout.Controls.Add(this.lblOwnerValue, 1, 2);
            this.tblMetadataLayout.Controls.Add(this.lblSizeTitle, 0, 3);
            this.tblMetadataLayout.Controls.Add(this.lblSizeValue, 1, 3);
            this.tblMetadataLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tblMetadataLayout.Location = new System.Drawing.Point(10, 28);
            this.tblMetadataLayout.Name = "tblMetadataLayout";
            this.tblMetadataLayout.RowCount = 4;
            this.tblMetadataLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.tblMetadataLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.tblMetadataLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.tblMetadataLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.tblMetadataLayout.Size = new System.Drawing.Size(500, 112);
            this.tblMetadataLayout.TabIndex = 0;
            // 
            // lblCollationTitle
            // 
            this.lblCollationTitle.AutoSize = true;
            this.lblCollationTitle.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblCollationTitle.Location = new System.Drawing.Point(3, 4);
            this.lblCollationTitle.Margin = new System.Windows.Forms.Padding(3, 4, 12, 4);
            this.lblCollationTitle.Name = "lblCollationTitle";
            this.lblCollationTitle.Size = new System.Drawing.Size(115, 17);
            this.lblCollationTitle.TabIndex = 0;
            this.lblCollationTitle.Text = "Current Collation:";
            // 
            // lblCollationValue
            // 
            this.lblCollationValue.AutoSize = true;
            this.lblCollationValue.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblCollationValue.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
            this.lblCollationValue.Location = new System.Drawing.Point(133, 4);
            this.lblCollationValue.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.lblCollationValue.Name = "lblCollationValue";
            this.lblCollationValue.Size = new System.Drawing.Size(13, 17);
            this.lblCollationValue.TabIndex = 4;
            this.lblCollationValue.Text = "-";
            // 
            // lblCompatLevelTitle
            // 
            this.lblCompatLevelTitle.AutoSize = true;
            this.lblCompatLevelTitle.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblCompatLevelTitle.Location = new System.Drawing.Point(3, 29);
            this.lblCompatLevelTitle.Margin = new System.Windows.Forms.Padding(3, 4, 12, 4);
            this.lblCompatLevelTitle.Name = "lblCompatLevelTitle";
            this.lblCompatLevelTitle.Size = new System.Drawing.Size(133, 17);
            this.lblCompatLevelTitle.TabIndex = 1;
            this.lblCompatLevelTitle.Text = "Compatibility Level:";
            // 
            // lblCompatLevelValue
            // 
            this.lblCompatLevelValue.AutoSize = true;
            this.lblCompatLevelValue.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblCompatLevelValue.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
            this.lblCompatLevelValue.Location = new System.Drawing.Point(151, 29);
            this.lblCompatLevelValue.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.lblCompatLevelValue.Name = "lblCompatLevelValue";
            this.lblCompatLevelValue.Size = new System.Drawing.Size(13, 17);
            this.lblCompatLevelValue.TabIndex = 5;
            this.lblCompatLevelValue.Text = "-";
            // 
            // lblOwnerTitle
            // 
            this.lblOwnerTitle.AutoSize = true;
            this.lblOwnerTitle.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblOwnerTitle.Location = new System.Drawing.Point(3, 54);
            this.lblOwnerTitle.Margin = new System.Windows.Forms.Padding(3, 4, 12, 4);
            this.lblOwnerTitle.Name = "lblOwnerTitle";
            this.lblOwnerTitle.Size = new System.Drawing.Size(53, 17);
            this.lblOwnerTitle.TabIndex = 2;
            this.lblOwnerTitle.Text = "Owner:";
            // 
            // lblOwnerValue
            // 
            this.lblOwnerValue.AutoSize = true;
            this.lblOwnerValue.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblOwnerValue.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
            this.lblOwnerValue.Location = new System.Drawing.Point(71, 54);
            this.lblOwnerValue.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.lblOwnerValue.Name = "lblOwnerValue";
            this.lblOwnerValue.Size = new System.Drawing.Size(13, 17);
            this.lblOwnerValue.TabIndex = 6;
            this.lblOwnerValue.Text = "-";
            // 
            // lblSizeTitle
            // 
            this.lblSizeTitle.AutoSize = true;
            this.lblSizeTitle.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblSizeTitle.Location = new System.Drawing.Point(3, 79);
            this.lblSizeTitle.Margin = new System.Windows.Forms.Padding(3, 4, 12, 4);
            this.lblSizeTitle.Name = "lblSizeTitle";
            this.lblSizeTitle.Size = new System.Drawing.Size(36, 17);
            this.lblSizeTitle.TabIndex = 3;
            this.lblSizeTitle.Text = "Size:";
            // 
            // lblSizeValue
            // 
            this.lblSizeValue.AutoSize = true;
            this.lblSizeValue.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblSizeValue.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(71)))), ((int)(((byte)(85)))), ((int)(((byte)(105)))));
            this.lblSizeValue.Location = new System.Drawing.Point(54, 79);
            this.lblSizeValue.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
            this.lblSizeValue.Name = "lblSizeValue";
            this.lblSizeValue.Size = new System.Drawing.Size(13, 17);
            this.lblSizeValue.TabIndex = 7;
            this.lblSizeValue.Text = "-";
            // 
            // lblTargetConfigHeader
            // 
            this.lblTargetConfigHeader.AutoSize = true;
            this.lblTargetConfigHeader.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblTargetConfigHeader.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(41)))), ((int)(((byte)(59)))));
            this.lblTargetConfigHeader.Location = new System.Drawing.Point(0, 232);
            this.lblTargetConfigHeader.Margin = new System.Windows.Forms.Padding(0, 8, 0, 8);
            this.lblTargetConfigHeader.Name = "lblTargetConfigHeader";
            this.lblTargetConfigHeader.Size = new System.Drawing.Size(166, 21);
            this.lblTargetConfigHeader.TabIndex = 3;
            this.lblTargetConfigHeader.Text = "Target Configuration";
            // 
            // tblTargetConfigLayout
            // 
            this.tblTargetConfigLayout.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tblTargetConfigLayout.AutoSize = true;
            this.tblTargetConfigLayout.ColumnCount = 2;
            this.tblTargetConfigLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tblTargetConfigLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tblTargetConfigLayout.Controls.Add(this.lblTargetCollation, 0, 0);
            this.tblTargetConfigLayout.Controls.Add(this.txtTargetCollation, 0, 1);
            this.tblTargetConfigLayout.Controls.Add(this.lblBackupDir, 1, 0);
            this.tblTargetConfigLayout.Controls.Add(this.txtBackupDirectory, 1, 1);
            this.tblTargetConfigLayout.Location = new System.Drawing.Point(0, 261);
            this.tblTargetConfigLayout.Margin = new System.Windows.Forms.Padding(0, 0, 0, 16);
            this.tblTargetConfigLayout.Name = "tblTargetConfigLayout";
            this.tblTargetConfigLayout.RowCount = 2;
            this.tblTargetConfigLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.tblTargetConfigLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.tblTargetConfigLayout.Size = new System.Drawing.Size(520, 60);
            this.tblTargetConfigLayout.TabIndex = 4;
            // 
            // lblTargetCollation
            // 
            this.lblTargetCollation.AutoSize = true;
            this.lblTargetCollation.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblTargetCollation.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.lblTargetCollation.Location = new System.Drawing.Point(0, 0);
            this.lblTargetCollation.Margin = new System.Windows.Forms.Padding(0, 0, 8, 4);
            this.lblTargetCollation.Name = "lblTargetCollation";
            this.lblTargetCollation.Size = new System.Drawing.Size(120, 15);
            this.lblTargetCollation.TabIndex = 0;
            this.lblTargetCollation.Text = "New Collation Name";
            // 
            // txtTargetCollation
            // 
            this.txtTargetCollation.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtTargetCollation.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtTargetCollation.Location = new System.Drawing.Point(0, 19);
            this.txtTargetCollation.Margin = new System.Windows.Forms.Padding(0, 0, 8, 12);
            this.txtTargetCollation.Name = "txtTargetCollation";
            this.txtTargetCollation.Size = new System.Drawing.Size(252, 25);
            this.txtTargetCollation.TabIndex = 1;
            this.toolTip.SetToolTip(this.txtTargetCollation, "E.g., SQL_Latin1_General_CP1_CI_AS");
            this.txtTargetCollation.TextChanged += new System.EventHandler(this.txtTargetCollation_TextChanged);
            // 
            // lblBackupDir
            // 
            this.lblBackupDir.AutoSize = true;
            this.lblBackupDir.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblBackupDir.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(51)))), ((int)(((byte)(65)))), ((int)(((byte)(85)))));
            this.lblBackupDir.Location = new System.Drawing.Point(268, 0);
            this.lblBackupDir.Margin = new System.Windows.Forms.Padding(8, 0, 0, 4);
            this.lblBackupDir.Name = "lblBackupDir";
            this.lblBackupDir.Size = new System.Drawing.Size(97, 15);
            this.lblBackupDir.TabIndex = 2;
            this.lblBackupDir.Text = "Backup Directory";
            // 
            // txtBackupDirectory
            // 
            this.txtBackupDirectory.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtBackupDirectory.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtBackupDirectory.Location = new System.Drawing.Point(268, 19);
            this.txtBackupDirectory.Margin = new System.Windows.Forms.Padding(8, 0, 0, 12);
            this.txtBackupDirectory.Name = "txtBackupDirectory";
            this.txtBackupDirectory.Size = new System.Drawing.Size(252, 25);
            this.txtBackupDirectory.TabIndex = 3;
            this.txtBackupDirectory.TextChanged += new System.EventHandler(this.txtBackupDirectory_TextChanged);
            // 
            // btnProcess
            // 
            this.btnProcess.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(16)))), ((int)(((byte)(185)))), ((int)(((byte)(129)))));
            this.btnProcess.FlatAppearance.BorderSize = 0;
            this.btnProcess.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnProcess.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnProcess.ForeColor = System.Drawing.Color.White;
            this.btnProcess.Location = new System.Drawing.Point(0, 337);
            this.btnProcess.Margin = new System.Windows.Forms.Padding(0, 0, 0, 12);
            this.btnProcess.Name = "btnProcess";
            this.btnProcess.Size = new System.Drawing.Size(220, 36);
            this.btnProcess.TabIndex = 5;
            this.btnProcess.Text = "Process Collation Change";
            this.btnProcess.UseVisualStyleBackColor = false;
            this.btnProcess.Click += new System.EventHandler(this.btnProcess_Click);
            // 
            // lblWarning
            // 
            this.lblWarning.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblWarning.AutoSize = true;
            this.lblWarning.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblWarning.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(220)))), ((int)(((byte)(38)))), ((int)(((byte)(38)))));
            this.lblWarning.Location = new System.Drawing.Point(0, 385);
            this.lblWarning.Margin = new System.Windows.Forms.Padding(0, 0, 0, 12);
            this.lblWarning.MaximumSize = new System.Drawing.Size(500, 0);
            this.lblWarning.Name = "lblWarning";
            this.lblWarning.Size = new System.Drawing.Size(500, 30);
            this.lblWarning.TabIndex = 6;
            this.lblWarning.Text = "Warning: Modifying collation will set the database to SINGLE_USER mode briefly and may drop active connections.";
            // 
            // lblDashboardHeader
            // 
            this.lblDashboardHeader.AutoSize = true;
            this.lblDashboardHeader.Font = new System.Drawing.Font("Segoe UI", 15.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblDashboardHeader.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(41)))), ((int)(((byte)(59)))));
            this.lblDashboardHeader.Location = new System.Drawing.Point(25, 20);
            this.lblDashboardHeader.Name = "lblDashboardHeader";
            this.lblDashboardHeader.Size = new System.Drawing.Size(217, 30);
            this.lblDashboardHeader.TabIndex = 0;
            this.lblDashboardHeader.Text = "Database Operations";
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(900, 626);
            this.Controls.Add(this.panelDashboard);
            this.Controls.Add(this.panelAuth);
            this.Controls.Add(this.statusStrip);
            this.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.MinimumSize = new System.Drawing.Size(800, 600);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Collation Modifier Utility";
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.panelAuth.ResumeLayout(false);
            this.panelAuth.PerformLayout();
            this.panelDashboard.ResumeLayout(false);
            this.panelDashboard.PerformLayout();
            this.panelDashboardContent.ResumeLayout(false);
            this.panelDashboardContent.PerformLayout();
            this.panelMetadata.ResumeLayout(false);
            this.panelMetadata.PerformLayout();
            this.tblMetadataLayout.ResumeLayout(false);
            this.tblMetadataLayout.PerformLayout();
            this.tblTargetConfigLayout.ResumeLayout(false);
            this.tblTargetConfigLayout.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel statusLabel;
        private System.Windows.Forms.ToolStripProgressBar progressBar;
        private System.Windows.Forms.TableLayoutPanel panelAuth;
        private System.Windows.Forms.Label lblAuthHeader;
        private System.Windows.Forms.Label lblServerName;
        private System.Windows.Forms.TextBox txtServerName;
        private System.Windows.Forms.Label lblAuthType;
        private System.Windows.Forms.ComboBox cmbAuthType;
        private System.Windows.Forms.Label lblUsername;
        private System.Windows.Forms.TextBox txtUsername;
        private System.Windows.Forms.Label lblPassword;
        private System.Windows.Forms.TextBox txtPassword;
        private System.Windows.Forms.Button btnConnect;
        private System.Windows.Forms.Panel panelDashboard;
        private System.Windows.Forms.Label lblDashboardHeader;
        private System.Windows.Forms.Label lblDisconnectedOverlay;
        private System.Windows.Forms.TableLayoutPanel panelDashboardContent;
        private System.Windows.Forms.Label lblSelectDb;
        private System.Windows.Forms.ComboBox cmbDatabases;
        private System.Windows.Forms.GroupBox panelMetadata;
        private System.Windows.Forms.TableLayoutPanel tblMetadataLayout;
        private System.Windows.Forms.Label lblCollationTitle;
        private System.Windows.Forms.Label lblCompatLevelTitle;
        private System.Windows.Forms.Label lblOwnerTitle;
        private System.Windows.Forms.Label lblSizeTitle;
        private System.Windows.Forms.Label lblCollationValue;
        private System.Windows.Forms.Label lblCompatLevelValue;
        private System.Windows.Forms.Label lblOwnerValue;
        private System.Windows.Forms.Label lblSizeValue;
        private System.Windows.Forms.Label lblTargetConfigHeader;
        private System.Windows.Forms.TableLayoutPanel tblTargetConfigLayout;
        private System.Windows.Forms.Label lblTargetCollation;
        private System.Windows.Forms.TextBox txtTargetCollation;
        private System.Windows.Forms.Label lblBackupDir;
        private System.Windows.Forms.TextBox txtBackupDirectory;
        private System.Windows.Forms.Button btnProcess;
        private System.Windows.Forms.Label lblWarning;
        private System.Windows.Forms.ToolTip toolTip;
    }
}
