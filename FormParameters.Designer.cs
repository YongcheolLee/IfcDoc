﻿namespace IfcDoc
{
    partial class FormParameters
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormParameters));
            this.ctlParameters = new IfcDoc.CtlParameters();
            this.SuspendLayout();
            // 
            // ctlParameters
            // 
            this.ctlParameters.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.ctlParameters.ConceptItem = null;
            this.ctlParameters.ConceptLeaf = null;
            this.ctlParameters.ConceptRoot = null;
            this.ctlParameters.CurrentInstance = null;
            resources.ApplyResources(this.ctlParameters, "ctlParameters");
            this.ctlParameters.Name = "ctlParameters";
            this.ctlParameters.Project = null;
            // 
            // FormParameters
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.ctlParameters);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FormParameters";
            this.ShowInTaskbar = false;
            this.ResumeLayout(false);

        }

        #endregion

        private CtlParameters ctlParameters;

    }
}