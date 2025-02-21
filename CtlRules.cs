﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

using IfcDoc.Schema;
using IfcDoc.Schema.DOC;

namespace IfcDoc
{
    public partial class CtlRules : UserControl
    {
        DocProject m_project;
        DocTemplateDefinition m_parent;
        DocTemplateDefinition m_template;
        DocAttribute m_attribute;
        DocModelRule m_selection;
        SEntity m_instance;

        public event EventHandler SelectionChanged;
        public event EventHandler ContentChanged;

        public CtlRules()
        {
            InitializeComponent();
        }

        public DocProject Project
        {
            get
            {
                return this.m_project;
            }
            set
            {
                this.m_project = value;
            }
        }

        public DocTemplateDefinition BaseTemplate
        {
            get
            {
                return this.m_parent;
            }
            set
            {
                this.m_parent = value;
            }
        }

        public DocTemplateDefinition Template
        {
            get
            {
                return this.m_template;
            }
            set
            {
                this.m_template = value;
                LoadTemplateGraph();
                UpdateCommands();
            }
        }

        public DocAttribute Attribute
        {
            get
            {
                return this.m_attribute;
            }
            set
            {
                this.m_attribute = value;
                UpdateCommands();
            }
        }

        public DocModelRule Selection
        {
            get
            {
                return this.m_selection;
            }
            set
            {
                if (this.m_selection == value)
                    return;

                this.m_selection = value;

                if (this.m_selection == null && this.treeViewTemplate.Nodes.Count > 0)
                {
                    this.treeViewTemplate.SelectedNode = this.treeViewTemplate.Nodes[0];
                }
                else
                {

                    foreach (TreeNode tn in this.treeViewTemplate.Nodes)
                    {
                        UpdateTreeNodeSelection(tn);
                    }
                }

                UpdateCommands();
            }
        }

        public SEntity CurrentInstance
        {
            get
            {
                return this.m_instance;
            }
            set
            {
                this.m_instance = value;

                foreach (TreeNode tn in this.treeViewTemplate.Nodes)
                {
                    UpdateTreeNodeValidation(tn);
                }
            }
        }

        private void UpdateTreeNodeValidation(TreeNode tn)
        {
            //...

            foreach (TreeNode ts in tn.Nodes)
            {
                UpdateTreeNodeValidation(ts);
            }
        }

        private void UpdateTreeNodeSelection(TreeNode tn)
        {
            if (tn.Tag == this.Selection)
            {
                this.treeViewTemplate.SelectedNode = tn;
                return;
            }

            foreach (TreeNode ts in tn.Nodes)
            {
                UpdateTreeNodeSelection(ts);
            }
        }

        private void LoadTemplateGraph()
        {
            this.treeViewTemplate.BeginUpdate();
            this.treeViewTemplate.Nodes.Clear();

            if (this.m_template == null || String.IsNullOrEmpty(this.m_template.Type))
            {
                this.treeViewTemplate.EndUpdate();
                return;
            }

            // add root rule according to applicable entity
            TreeNode tnRoot = new TreeNode();
            tnRoot.Tag = this.m_template;
            tnRoot.Text = this.m_template.Type;
            tnRoot.ImageIndex = 0;
            tnRoot.SelectedImageIndex = 0;
            tnRoot.ForeColor = Color.Gray; // top node is gray; cannot be edited

            this.treeViewTemplate.Nodes.Add(tnRoot);
            this.treeViewTemplate.SelectedNode = tnRoot;

            // load explicit rules
            if (this.m_template.Rules != null)
            {
                foreach (DocModelRule rule in this.m_template.Rules)
                {
                    this.LoadTemplateGraph(tnRoot, rule);
                }
            }

            this.treeViewTemplate.ExpandAll();
            this.treeViewTemplate.EndUpdate();
        }

        private TreeNode LoadTemplateGraph(TreeNode tnParent, DocModelRule docRule)
        {
            TreeNode tnRule = LoadTemplateRuleNode(tnParent, docRule, docRule.Name);
            UpdateTemplateGraph(tnRule);
            tnRule.Nodes.Clear();

            foreach (DocModelRule docSub in docRule.Rules)
            {
                LoadTemplateGraph(tnRule, docSub);
            }

            if (docRule is DocModelRuleEntity)
            {
                DocModelRuleEntity dme = (DocModelRuleEntity)docRule;
                foreach (DocTemplateDefinition dtd in dme.References)
                {
                    TreeNode tnTemplate = LoadTemplateRuleNode(tnRule, dtd, dtd.Name);
                    if (dtd.Rules != null)
                    {
                        foreach (DocModelRule docTemplateRule in dtd.Rules)
                        {
                            LoadTemplateGraph(tnTemplate, docTemplateRule);
                        }
                    }
                }
            }

            return tnRule;
        }

        private void UpdateTemplateGraph(TreeNode tnRule)
        {
            DocModelRule docRule = (DocModelRule)tnRule.Tag;
            tnRule.Text = docRule.Name;

            if (this.m_parent != null)
            {
                DocModelRule[] objpath = this.m_parent.GetRulePath(tnRule.FullPath);
                if (objpath != null && objpath[objpath.Length - 1] != null)
                {
                    tnRule.ForeColor = Color.Gray;
                }
            }

            string tooltip = docRule.Name;
            // decorative text doesn't allow treeview path to work -- use tooltip in UI now instead
            //tooltip += docRule.GetCardinalityExpression();
            if (!String.IsNullOrEmpty(docRule.Identification))
            {
                tooltip += " <" + docRule.Identification + ">";
                tnRule.BackColor = Color.LightBlue; // mark parameter
            }
            else
            {
                tnRule.BackColor = Color.Empty;
            }
            tnRule.ToolTipText = tooltip;
        }

        private TreeNode LoadTemplateRuleNode(TreeNode parent, object tag, string text)
        {
            // if existing, then return
            foreach (TreeNode tnExist in parent.Nodes)
            {
                if (tnExist.Tag == tag)
                    return tnExist;
            }

            TreeNode tn = new TreeNode();
            tn.Tag = tag;
            tn.Text = text;

            if (tag is DocModelRuleEntity)
            {
                tn.ImageIndex = 0;
            }
            else if (tag is DocModelRuleAttribute)
            {
                tn.ImageIndex = 1;
            }
            else if (tag is DocModelRuleConstraint)
            {
                tn.ImageIndex = 2;
            }
            else if (tag is DocTemplateDefinition)
            {
                tn.ImageIndex = 3;
            }
            tn.SelectedImageIndex = tn.ImageIndex;

            parent.Nodes.Add(tn);

            return tn;
        }

        private void toolStripButtonTemplateInsert_Click(object sender, EventArgs e)
        {
            this.DoInsert();
        }

        public void DoInsert()
        {
            if (this.m_template == null)
                return;

            if (this.treeViewTemplate.Nodes.Count == 0)
            {
                DocEntity docEntityBase = null;
                if (this.m_parent is DocTemplateDefinition)
                {
                    string classname = ((DocTemplateDefinition)this.m_parent).Type;
                    docEntityBase = this.m_project.GetDefinition(classname) as DocEntity;
                }

                // get selected entity
                DocEntity docEntityThis = this.m_project.GetDefinition(this.m_template.Type) as DocEntity;
                
                using (FormSelectEntity form = new FormSelectEntity(docEntityBase, docEntityThis, this.m_project, SelectDefinitionOptions.Entity))
                {
                    DialogResult res = form.ShowDialog(this);
                    if (res == DialogResult.OK && form.SelectedEntity != null)
                    {
                        this.m_template.Type = form.SelectedEntity.Name;
                        this.LoadTemplateGraph();
                        this.ContentChanged(this, EventArgs.Empty);
                    }
                }

                return;
            }

            if (this.m_attribute != null && !String.IsNullOrEmpty(this.m_attribute.DefinedType))
            {
                DocTemplateDefinition docTemplate = this.m_template;
                DocAttribute docAttribute = this.m_attribute;

                string entityname = null;
                switch (this.m_attribute.DefinedType)
                {
                    case "BOOLEAN":
                    case "LOGICAL":
                    case "BINARY":
                    case "STRING":
                    case "REAL":
                    case "INTEGER":
                    case "NUMBER":
                        entityname = this.m_attribute.DefinedType;
                        break;

                    default:
                        {
                            // qualify schema
                            DocObject docobj = null;

                            if (!String.IsNullOrEmpty(this.m_template.Code))
                            {
                                foreach(DocSection docSection in this.m_project.Sections)
                                {
                                    foreach (DocSchema docSchema in docSection.Schemas)
                                    {
                                        if (docSchema.Name.Equals(this.m_template.Code, StringComparison.OrdinalIgnoreCase))
                                        {
                                            docobj = docSchema.GetDefinition(docAttribute.DefinedType);
                                            break;
                                        }
                                    }
                                }
                            }

                            if (docobj == null)
                            {
                                docobj = this.m_project.GetDefinition(docAttribute.DefinedType);
                            }

                            using (FormSelectEntity form = new FormSelectEntity((DocDefinition)docobj, null, this.m_project, SelectDefinitionOptions.Entity | SelectDefinitionOptions.Type))
                            {
                                DialogResult res = form.ShowDialog(this);
                                if (res == DialogResult.OK && form.SelectedEntity != null)
                                {
                                    entityname = form.SelectedEntity.Name;
                                }
                            }
                            break;
                        }
                }

                if (entityname != null)
                {
                    // get or add attribute rule
                    TreeNode tn = this.treeViewTemplate.SelectedNode;
                    DocModelRuleAttribute docRuleAtt = null;
                    if (this.treeViewTemplate.SelectedNode.Tag is DocModelRuleAttribute)
                    {
                        docRuleAtt = (DocModelRuleAttribute)this.treeViewTemplate.SelectedNode.Tag;
                    }
                    else
                    {
                        docRuleAtt = new DocModelRuleAttribute();
                        docRuleAtt.Name = docAttribute.Name;

                        if (this.treeViewTemplate.SelectedNode.Tag is DocModelRuleEntity)
                        {
                            DocModelRuleEntity docRuleEnt = (DocModelRuleEntity)this.treeViewTemplate.SelectedNode.Tag;
                            docRuleEnt.Rules.Add(docRuleAtt);
                        }
                        else if (this.treeViewTemplate.SelectedNode.Tag is DocTemplateDefinition)
                        {
                            docTemplate.Rules.Add(docRuleAtt);
                        }

                        tn = this.LoadTemplateGraph(tn, docRuleAtt);
                    }

                    // get and add entity rule
                    DocModelRuleEntity docRuleEntity = new DocModelRuleEntity();
                    docRuleEntity.Name = entityname;
                    docRuleAtt.Rules.Add(docRuleEntity);
                    this.treeViewTemplate.SelectedNode = this.LoadTemplateGraph(tn, docRuleEntity);

                    // copy to child templates
                    docTemplate.PropagateRule(this.treeViewTemplate.SelectedNode.FullPath);

                    this.m_selection = docRuleEntity;
                    this.ContentChanged(this, EventArgs.Empty);
                    this.SelectionChanged(this, EventArgs.Empty);
                }
            }
            else
            {
                // pick attribute, including attribute that may be on subtype
                DocModelRule rule = null;
                if (this.treeViewTemplate.SelectedNode != null)
                {
                    rule = this.treeViewTemplate.SelectedNode.Tag as DocModelRule;
                }

                //if (rule == null)
                //   return;

                DocTemplateDefinition docTemplate = (DocTemplateDefinition)this.m_template;

                string typename = null;
                if (rule is DocModelRuleEntity)
                {
                    DocModelRuleEntity docRuleEntity = (DocModelRuleEntity)rule;
                    typename = docRuleEntity.Name;
                }
                else
                {
                    // get applicable entity of target (or parent entity rule)
                    typename = docTemplate.Type;
                }

                DocEntity docEntity = this.m_project.GetDefinition(typename) as DocEntity;
                if (docEntity == null)
                {
                    // launch dialog for constraint
                    using (FormConstraint form = new FormConstraint())
                    {
                        DialogResult res = form.ShowDialog(this);
                        if (res == DialogResult.OK)
                        {
                            DocModelRuleConstraint docRuleConstraint = new DocModelRuleConstraint();
                            rule.Rules.Add(docRuleConstraint);
                            docRuleConstraint.Description = form.Expression;
                            docRuleConstraint.Name = form.Expression; // for viewing

                            this.treeViewTemplate.SelectedNode = this.LoadTemplateGraph(this.treeViewTemplate.SelectedNode, docRuleConstraint);

                            // copy to child templates
                            docTemplate.PropagateRule(this.treeViewTemplate.SelectedNode.FullPath);
                        }
                    }
                }
                else
                {
                    // launch dialog to pick attribute of entity
                    using (FormSelectAttribute form = new FormSelectAttribute(docEntity, this.m_project, null, true))
                    {
                        DialogResult res = form.ShowDialog(this);
                        if (res == DialogResult.OK && form.Selection != null)
                        {
                            // then add and update tree
                            DocModelRuleAttribute docRuleAttr = new DocModelRuleAttribute();
                            docRuleAttr.Name = form.Selection;
                            if (rule != null)
                            {
                                rule.Rules.Add(docRuleAttr);
                            }
                            else
                            {
                                docTemplate.Rules.Add(docRuleAttr);
                            }
                            this.treeViewTemplate.SelectedNode = this.LoadTemplateGraph(this.treeViewTemplate.SelectedNode, docRuleAttr);

                            // copy to child templates
                            docTemplate.PropagateRule(this.treeViewTemplate.SelectedNode.FullPath);
                        }
                    }
                }

            }
        }

        private void toolStripButtonTemplateRemove_Click(object sender, EventArgs e)
        {
            if (this.treeViewTemplate.SelectedNode.Tag is DocTemplateDefinition)
            {
                DocTemplateDefinition dtd = (DocTemplateDefinition)this.treeViewTemplate.SelectedNode.Tag;
                if (treeViewTemplate.SelectedNode.Parent != null)
                {
                    DocModelRuleEntity dme = (DocModelRuleEntity)this.treeViewTemplate.SelectedNode.Parent.Tag;
                    dme.References.Remove(dtd);
                    this.treeViewTemplate.SelectedNode.Remove();
                }
                else
                {
                    this.m_template.Type = null;
                    this.treeViewTemplate.Nodes.Clear();
                }
                UpdateCommands();
                this.ContentChanged(this, EventArgs.Empty);
                return;
            }

            DocModelRule ruleTarget = this.treeViewTemplate.SelectedNode.Tag as DocModelRule;
            DocModelRule ruleParent = null;

            if (this.treeViewTemplate.SelectedNode.Parent != null)
            {
                ruleParent = this.treeViewTemplate.SelectedNode.Parent.Tag as DocModelRule;
            }

            if (ruleParent != null)
            {
                ruleParent.Rules.Remove(ruleTarget);
            }
            else
            {
                this.m_template.Rules.Remove(ruleTarget);
            }

            // copy to child templates (before clearing selection)
            this.m_template.PropagateRule(this.treeViewTemplate.SelectedNode.FullPath);

            ruleTarget.Delete();
            this.treeViewTemplate.SelectedNode.Remove();

            this.ContentChanged(this, EventArgs.Empty);
        }

        private void toolStripButtonTemplateUpdate_Click(object sender, EventArgs e)
        {
            if (this.treeViewTemplate.SelectedNode != null && this.treeViewTemplate.SelectedNode.Tag is DocModelRule)
            {
                DocModelRule docRule = (DocModelRule)this.treeViewTemplate.SelectedNode.Tag;
                if (docRule is DocModelRuleConstraint)
                {
                    using (FormConstraint form = new FormConstraint())
                    {
                        form.Expression = docRule.Description;
                        DialogResult res = form.ShowDialog(this);
                        if (res == DialogResult.OK)
                        {
                            docRule.Description = form.Expression;
                            docRule.Name = form.Expression; // repeat for visibility
                        }
                    }
                }
                else
                {
                    using (FormRule form = new FormRule(docRule))
                    {
                        form.ShowDialog(this);
                    }
                }

                // update text in treeview
                this.UpdateTemplateGraph(this.treeViewTemplate.SelectedNode);

                // propagate rule
                this.m_template.PropagateRule(this.treeViewTemplate.SelectedNode.FullPath);
            }
        }

        private void UpdateCommands()
        {
            if (this.m_template == null)
            {
                this.toolStripButtonTemplateInsert.Enabled = false;
                this.toolStripButtonTemplateRemove.Enabled = false;
                this.toolStripButtonTemplateUpdate.Enabled = false;
                return;
            }

            bool locked = (this.treeViewTemplate.SelectedNode != null && this.treeViewTemplate.SelectedNode.ForeColor == Color.Gray);

            bool insert = true;
            if (this.treeViewTemplate.SelectedNode != null && this.treeViewTemplate.SelectedNode.Tag is DocModelRuleConstraint)
            {
                insert = false;
            }

            this.toolStripButtonTemplateInsert.Enabled = insert;
            this.toolStripButtonTemplateRemove.Enabled = (this.Selection != null && !locked) || (this.treeViewTemplate.SelectedNode != null && this.treeViewTemplate.SelectedNode.Tag is DocTemplateDefinition);
            this.toolStripButtonTemplateUpdate.Enabled = ((this.Selection is DocModelRuleAttribute || (this.Selection is DocModelRuleEntity)) && !locked);
            this.toolStripButtonRuleRef.Enabled = (this.Selection is DocModelRuleEntity);

            TreeNode tnSelect = this.treeViewTemplate.SelectedNode;
            TreeNode tnParent = null;
            if(tnSelect != null)
            { 
                tnParent = this.treeViewTemplate.SelectedNode.Parent;
            }
            
            this.toolStripButtonMoveUp.Enabled = (tnParent != null && tnParent.Nodes.IndexOf(tnSelect) > 0) && !locked;
            this.toolStripButtonMoveDown.Enabled = (tnParent != null && tnParent.Nodes.IndexOf(tnSelect) < tnParent.Nodes.Count - 1) && !locked;
            this.toolStripButtonSetDefault.Enabled = (this.Selection is DocModelRuleAttribute || this.Selection is DocModelRuleEntity);
        }

        private void treeViewTemplate_AfterSelect(object sender, TreeViewEventArgs e)
        {
            this.m_attribute = null;
            this.m_selection = e.Node.Tag as DocModelRule;
            UpdateCommands();

            if (this.SelectionChanged != null)
            {
                this.SelectionChanged(this, EventArgs.Empty);
            }
        }

        private void toolStripButtonMoveUp_Click(object sender, EventArgs e)
        {
            MoveRule(-1);
        }

        private void toolStripButtonMoveDown_Click(object sender, EventArgs e)
        {
            MoveRule(+1);
        }

        private void MoveRule(int offset)
        {
            TreeNode tnSelect = this.treeViewTemplate.SelectedNode;
            TreeNode tnParent = tnSelect.Parent;
            DocModelRule ruleSelect = (DocModelRule)tnSelect.Tag;
            if (tnParent.Tag is DocModelRule)
            {
                DocModelRule ruleParent = (DocModelRule)tnParent.Tag;
                int index = ruleParent.Rules.IndexOf(ruleSelect);
                ruleParent.Rules.RemoveAt(index);

                index += offset;

                ruleParent.Rules.Insert(index, ruleSelect);
                tnSelect.Remove();
                tnParent.Nodes.Insert(index, tnSelect);
            }
            else if (tnParent.Tag is DocTemplateDefinition)
            {
                DocTemplateDefinition ruleParent = (DocTemplateDefinition)tnParent.Tag;
                int index = ruleParent.Rules.IndexOf(ruleSelect);
                ruleParent.Rules.RemoveAt(index);

                index += offset;

                ruleParent.Rules.Insert(index, ruleSelect);
                tnSelect.Remove();
                tnParent.Nodes.Insert(index, tnSelect);
            }

            this.treeViewTemplate.SelectedNode = tnSelect;
        }

        private void toolStripButtonRuleRef_Click(object sender, EventArgs e)
        {
            TreeNode tnSelect = this.treeViewTemplate.SelectedNode;
            DocModelRuleEntity docRule = (DocModelRuleEntity)tnSelect.Tag as DocModelRuleEntity;
            if (docRule == null)
                return;

            DocEntity docEntity = this.m_project.GetDefinition(docRule.Name) as DocEntity;
            if (docEntity == null)
                return;
            
            using (FormSelectTemplate form = new FormSelectTemplate(null, this.Project, docEntity))
            {
                if (form.ShowDialog(this) == DialogResult.OK && form.SelectedTemplate != null)
                {
                    // check for possible recursion
                    if(form.SelectedTemplate == this.m_template || form.SelectedTemplate.IsTemplateReferenced(this.m_template))
                    {
                        MessageBox.Show("Recursive template referencing is not supported.");
                        return;
                    }

                    docRule.References.Add(form.SelectedTemplate);

                    LoadTemplateGraph(tnSelect, docRule);
                }
            }
        }

        private void toolStripButtonSetDefault_Click(object sender, EventArgs e)
        {
            TreeNode tnSelect = this.treeViewTemplate.SelectedNode;
            if(tnSelect.Tag is DocModelRuleEntity)
            {
                DocModelRuleEntity rule = (DocModelRuleEntity)tnSelect.Tag;
                rule.Identification = "Value";
            }
            else if(tnSelect.Tag is DocModelRuleAttribute)
            {
                DocModelRuleAttribute rule = (DocModelRuleAttribute)tnSelect.Tag;
                rule.Identification = "Name";
            }

            if (this.ContentChanged != null)
            {
                this.ContentChanged(this, EventArgs.Empty);
            }
        }

    }
}
