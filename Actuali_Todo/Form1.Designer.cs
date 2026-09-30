namespace Actuali_Todo
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            lblVersionLocal = new Label();
            lblVersionServidor = new Label();
            lblTarifaEstado = new Label();
            btnBuscar = new Button();
            progressBar1 = new ProgressBar();
            comboBoxApps = new ComboBox();
            pictureBox1 = new PictureBox();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // lblVersionLocal
            // 
            lblVersionLocal.AutoSize = true;
            lblVersionLocal.Location = new Point(26, 321);
            lblVersionLocal.Name = "lblVersionLocal";
            lblVersionLocal.Size = new Size(107, 20);
            lblVersionLocal.TabIndex = 0;
            lblVersionLocal.Text = "Versión local: ?";
            // 
            // lblVersionServidor
            // 
            lblVersionServidor.AutoSize = true;
            lblVersionServidor.Location = new Point(26, 368);
            lblVersionServidor.Name = "lblVersionServidor";
            lblVersionServidor.Size = new Size(119, 20);
            lblVersionServidor.TabIndex = 1;
            lblVersionServidor.Text = "Última Versión: ?";
            // 
            // lblTarifaEstado
            // 
            lblTarifaEstado.AutoSize = true;
            lblTarifaEstado.Location = new Point(26, 400);
            lblTarifaEstado.Name = "lblTarifaEstado";
            lblTarifaEstado.Size = new Size(119, 20);
            lblTarifaEstado.TabIndex = 6;
            lblTarifaEstado.Text = "Tarifas: ?";
            // 
            // btnBuscar
            // 
            btnBuscar.Location = new Point(190, 253);
            btnBuscar.Name = "btnBuscar";
            btnBuscar.Size = new Size(158, 41);
            btnBuscar.TabIndex = 2;
            btnBuscar.Text = "Actualizar";
            btnBuscar.UseVisualStyleBackColor = true;
            btnBuscar.Click += btnBuscar_Click;
            // 
            // progressBar1
            // 
            progressBar1.Location = new Point(-3, 430);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new Size(542, 36);
            progressBar1.TabIndex = 3;
            // 
            // comboBoxApps
            // 
            comboBoxApps.FormattingEnabled = true;
            comboBoxApps.Items.AddRange(new object[] { "AUXILIARES", "CAPTURA", "NOMINA", "DEPRECIACION", "LIBROV", "ACUMULADO", "CONTROLAVC" });
            comboBoxApps.Location = new Point(165, 12);
            comboBoxApps.Name = "comboBoxApps";
            comboBoxApps.Size = new Size(210, 28);
            comboBoxApps.TabIndex = 4;
            comboBoxApps.Text = "Selecciona una aplicacion";
            comboBoxApps.SelectedIndexChanged += comboBoxApps_SelectedIndexChanged;
            // 
            // pictureBox1
            // 
            pictureBox1.Location = new Point(146, 46);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(242, 201);
            pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureBox1.TabIndex = 5;
            pictureBox1.TabStop = false;
            pictureBox1.Click += pictureBox1_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(540, 466);
            Controls.Add(pictureBox1);
            Controls.Add(comboBoxApps);
            Controls.Add(progressBar1);
            Controls.Add(btnBuscar);
            Controls.Add(lblVersionServidor);
            Controls.Add(lblVersionLocal);
            Controls.Add(lblTarifaEstado);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Name = "Form1";
            Text = "Actualizador Cliente";
            Load += Form1_Load;
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblVersionLocal;
        private System.Windows.Forms.Label lblVersionServidor;
        private System.Windows.Forms.Label lblTarifaEstado;
        private System.Windows.Forms.Button btnBuscar;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.ComboBox comboBoxApps;
        private PictureBox pictureBox1;
    }
}
