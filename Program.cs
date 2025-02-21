﻿// Name:        Program.cs
// Description: Command line utility entry point
// Author:      Tim Chipman
// Origination: Work performed for BuildingSmart by Constructivity.com LLC.
// Copyright:   (c) 2010 BuildingSmart International Ltd.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

using IfcDoc.Schema;
using IfcDoc.Schema.VEX;
using IfcDoc.Schema.DOC;
using IfcDoc.Schema.MVD;
using IfcDoc.Schema.PSD;
using IfcDoc.Schema.IFC;
using IfcDoc.Schema.SCH;
using IfcDoc.Format.MDB;
using IfcDoc.Format.SPF;
using IfcDoc.Format.HTM;

namespace IfcDoc
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.Run(new FormEdit(args));
        }

        static SCHEMATA GetSchemata(FormatSPF file)
        {
            foreach (SEntity entity in file.Instances.Values)
            {
                if (entity is SCHEMATA)
                {
                    return (SCHEMATA)entity;
                }
            }

            return null;
        }

        public static string DatabasePath
        {
            get
            {
                return System.IO.Path.GetDirectoryName(typeof(Program).Assembly.Location) + @"\ifcdoc.mdb";
            }
        }

        private static void ImportVexRectangle(DocDefinition docDefinition, RECTANGLE rectangle, SCHEMATA schemata)
        {
            if (rectangle != null && schemata.settings != null && schemata.settings.page != null)
            {
                int px = (int)(rectangle.x / schemata.settings.page.width);
                int py = (int)(rectangle.y / schemata.settings.page.height);
                int page = 1 + py * schemata.settings.page.nhorizontalpages + px;
                docDefinition.DiagramNumber = page;

                docDefinition.DiagramRectangle = new DocRectangle();
                docDefinition.DiagramRectangle.X = rectangle.x;
                docDefinition.DiagramRectangle.Y = rectangle.y;
                docDefinition.DiagramRectangle.Width = rectangle.dx;
                docDefinition.DiagramRectangle.Height = rectangle.dy;
            }
        }

        private static void ImportVexLine(OBJECT_LINE_LAYOUT layout, TEXT_LAYOUT textlayout, List<DocPoint> diagramLine, DocRectangle diagramLabel)
        {
            if (layout == null)
                return;

            diagramLine.Add(new DocPoint(layout.pline.startpoint.wx, layout.pline.startpoint.wy));

            int direction = layout.pline.startdirection;
            double posx = layout.pline.startpoint.wx;
            double posy = layout.pline.startpoint.wy;

            for (int i = 0; i < layout.pline.rpoint.Count; i++)
            {
                if (diagramLabel != null && textlayout != null &&
                    layout.textplacement != null &&
                    layout.textplacement.npos == i)
                {
                    diagramLabel.X = textlayout.x + posx;
                    diagramLabel.Y = textlayout.y + posy;
                    diagramLabel.Width = textlayout.width;
                    diagramLabel.Height = textlayout.height;
                }

                double offset = layout.pline.rpoint[i];
                if (direction == 1)
                {
                    posy += offset;
                    direction = 0;
                }
                else if (direction == 0)
                {
                    posx += offset;
                    direction = 1;
                }
                diagramLine.Add(new DocPoint(posx, posy));
            }
        }

        /// <summary>
        /// Creates a documentation schema from a VEX schema
        /// </summary>
        /// <param name="schemata">The VEX schema to import</param>
        /// <param name="project">The documentation project where the imported schema is to be created</param>
        /// <returns>The imported documentation schema</returns>
        internal static DocSchema ImportVex(SCHEMATA schemata, DocProject docProject, bool updateDescriptions)
        {
            DocSchema docSchema = docProject.RegisterSchema(schemata.name);
            if (updateDescriptions && schemata.comment != null && schemata.comment.text != null)
            {
                docSchema.Documentation = schemata.comment.text.text;
            }
            docSchema.DiagramPagesHorz = schemata.settings.page.nhorizontalpages;
            docSchema.DiagramPagesVert = schemata.settings.page.nverticalpages;

            // remember current types for deletion if they no longer exist
            List<DocObject> existing = new List<DocObject>();
            foreach (DocType doctype in docSchema.Types)
            {
                existing.Add(doctype);
            }
            foreach (DocEntity docentity in docSchema.Entities)
            {
                existing.Add(docentity);
            }
            foreach (DocFunction docfunction in docSchema.Functions)
            {
                existing.Add(docfunction);
            }
            foreach (DocGlobalRule docrule in docSchema.GlobalRules)
            {
                existing.Add(docrule);
            }

            docSchema.PageTargets.Clear();
            docSchema.SchemaRefs.Clear();
            docSchema.Comments.Clear();

            // remember references for fixing up attributes afterwords
            Dictionary<object, DocDefinition> mapRefs = new Dictionary<object, DocDefinition>();
            Dictionary<ATTRIBUTE_DEF, DocAttribute> mapAtts = new Dictionary<ATTRIBUTE_DEF, DocAttribute>();
            //Dictionary<SELECT_DEF, DocSelectItem> mapSels = new Dictionary<SELECT_DEF, DocSelectItem>();
            Dictionary<SELECT_DEF, DocLine> mapSL = new Dictionary<SELECT_DEF, DocLine>();
            Dictionary<SUBTYPE_DEF, DocLine> mapSubs = new Dictionary<SUBTYPE_DEF, DocLine>();
            Dictionary<PAGE_REF, DocPageTarget> mapPage = new Dictionary<PAGE_REF, DocPageTarget>();

            // entities and types
            foreach (object obj in schemata.objects)
            {
                if (obj is ENTITIES)
                {
                    ENTITIES ent = (ENTITIES)obj; // filter out orphaned entities having upper flags set
                    if ((ent.flag & 0xFFFF0000) == 0 && (ent.interfaceto == null || ent.interfaceto.theschema == null))
                    {
                        // create if doesn't exist
                        string name = ent.name.text;

                        string super = null;
                        if (ent.supertypes.Count > 0 && ent.supertypes[0].the_supertype is ENTITIES)
                        {
                            ENTITIES superent = (ENTITIES)ent.supertypes[0].the_supertype;
                            super = superent.name.text;
                        }

                        DocEntity docEntity = docSchema.RegisterEntity(name);
                        if (existing.Contains(docEntity))
                        {
                            existing.Remove(docEntity);
                        }
                        mapRefs.Add(obj, docEntity);

                        // clear out existing if merging
                        docEntity.BaseDefinition = null;
                        
                        foreach(DocSubtype docSub in docEntity.Subtypes)
                        {
                            docSub.Delete();
                        }
                        docEntity.Subtypes.Clear();
                        
                        foreach(DocUniqueRule docUniq in docEntity.UniqueRules)
                        {
                            docUniq.Delete();
                        }
                        docEntity.UniqueRules.Clear();
                        
                        foreach(DocLine docLine in docEntity.Tree)
                        {
                            docLine.Delete();
                        }
                        docEntity.Tree.Clear();

                        if (updateDescriptions && ent.comment != null && ent.comment.text != null)
                        {
                            docEntity.Documentation = ent.comment.text.text;
                        }
                        if (ent.supertypes.Count > 0 && ent.supertypes[0].the_supertype is ENTITIES)
                        {
                            docEntity.BaseDefinition = ((ENTITIES)ent.supertypes[0].the_supertype).name.text;
                        }

                        docEntity.EntityFlags = ent.flag;
                        if (ent.subtypes != null)
                        {
                            foreach (SUBTYPE_DEF sd in ent.subtypes)
                            {
                                // new (3.8): intermediate subtypes for diagrams
                                DocLine docLine = new DocLine();
                                ImportVexLine(sd.layout, null, docLine.DiagramLine, null);
                                docEntity.Tree.Add(docLine);

                                OBJECT od = (OBJECT)sd.the_subtype;

                                // tunnel through page ref
                                if (od is PAGE_REF_TO)
                                {
                                    od = ((PAGE_REF_TO)od).pageref;
                                }

                                if (od is TREE)
                                {
                                    TREE tree = (TREE)od;
                                    foreach (OBJECT o in tree.list)
                                    {
                                        OBJECT os = o;
                                        OBJECT_LINE_LAYOUT linelayout = null;

                                        if (o is SUBTYPE_DEF)
                                        {
                                            SUBTYPE_DEF osd = (SUBTYPE_DEF)o;
                                            linelayout = osd.layout;

                                            os = ((SUBTYPE_DEF)o).the_subtype;
                                        }

                                        if (os is PAGE_REF_TO)
                                        {
                                            os = ((PAGE_REF_TO)os).pageref;
                                        }

                                        if (os is ENTITIES)
                                        {
                                            DocSubtype docSub = new DocSubtype();
                                            docSub.DefinedType = ((ENTITIES)os).name.text;
                                            docEntity.Subtypes.Add(docSub);

                                            DocLine docSubline = new DocLine();
                                            docLine.Tree.Add(docSubline);

                                            if (o is SUBTYPE_DEF)
                                            {
                                                mapSubs.Add((SUBTYPE_DEF)o, docSubline);
                                            }

                                            ImportVexLine(linelayout, null, docSubline.DiagramLine, null);
                                        }
                                        else
                                        {
                                            Debug.Assert(false);
                                        }
                                    }
                                }
                                else if (od is ENTITIES)
                                {
                                    DocSubtype docInt = new DocSubtype();
                                    docEntity.Subtypes.Add(docInt);

                                    docInt.DefinedType = ((ENTITIES)od).name.text;
                                }
                                else
                                {
                                    Debug.Assert(false);
                                }
                            }
                        }

                        // determine EXPRESS-G page based on placement (required for generating hyperlinks)
                        if (ent.layout != null)
                        {
                            ImportVexRectangle(docEntity, ent.layout.rectangle, schemata);
                        }

                        if (ent.attributes != null)
                        {
                            List<DocAttribute> existingattr = new List<DocAttribute>();
                            foreach (DocAttribute docAttr in docEntity.Attributes)
                            {
                                existingattr.Add(docAttr);
                            }

                            // attributes are replaced, not merged (template don't apply here)                            
                            foreach (ATTRIBUTE_DEF attr in ent.attributes)
                            {
                                if (attr.name != null)
                                {
                                    DocAttribute docAttr = docEntity.RegisterAttribute(attr.name.text);
                                    mapAtts.Add(attr, docAttr);

                                    if (existingattr.Contains(docAttr))
                                    {
                                        existingattr.Remove(docAttr);
                                    }

                                    if (updateDescriptions && attr.comment != null && attr.comment.text != null)
                                    {
                                        docAttr.Documentation = attr.comment.text.text;
                                    }

                                    if (docAttr.DiagramLabel != null)
                                    {
                                        docAttr.DiagramLabel.Delete();
                                        docAttr.DiagramLabel = null;
                                    }

                                    foreach(DocPoint docPoint in docAttr.DiagramLine)
                                    {
                                        docPoint.Delete();
                                    }
                                    docAttr.DiagramLine.Clear();

                                    if (attr.layout != null)
                                    {
                                        if (attr.layout.pline != null)
                                        {
                                            // intermediate lines
                                            if (attr.layout.pline.rpoint != null)
                                            {
                                                docAttr.DiagramLabel = new DocRectangle();
                                                ImportVexLine(attr.layout, attr.name.layout, docAttr.DiagramLine, docAttr.DiagramLabel);
                                            }
                                        }
                                    }

                                    OBJECT def = attr.the_attribute;
                                    if (attr.the_attribute is PAGE_REF_TO)
                                    {
                                        PAGE_REF_TO pr = (PAGE_REF_TO)attr.the_attribute;
                                        def = pr.pageref;
                                    }

                                    if (def is DEFINED_TYPE)
                                    {
                                        DEFINED_TYPE dt = (DEFINED_TYPE)def;
                                        docAttr.DefinedType = dt.name.text;
                                    }
                                    else if (def is ENTITIES)
                                    {
                                        ENTITIES en = (ENTITIES)def;
                                        docAttr.DefinedType = en.name.text;
                                    }
                                    else if (def is ENUMERATIONS)
                                    {
                                        ENUMERATIONS en = (ENUMERATIONS)def;
                                        docAttr.DefinedType = en.name.text;
                                    }
                                    else if (def is SELECTS)
                                    {
                                        SELECTS en = (SELECTS)def;
                                        docAttr.DefinedType = en.name.text;
                                    }
                                    else if (def is PRIMITIVE_TYPE)
                                    {
                                        PRIMITIVE_TYPE en = (PRIMITIVE_TYPE)def;

                                        string length = "";
                                        if (en.constraints > 0)
                                        {
                                            length = " (" + en.constraints.ToString() + ")";
                                        }
                                        else if (en.constraints < 0)
                                        {
                                            int len = -en.constraints;
                                            length = " (" + len.ToString() + ") FIXED";
                                        }

                                        docAttr.DefinedType = en.name.text + length;
                                    }
                                    else if (def is SCHEMA_REF)
                                    {
                                        SCHEMA_REF en = (SCHEMA_REF)def;
                                        docAttr.DefinedType = en.name.text;
                                    }
                                    else
                                    {
                                        Debug.Assert(false);
                                    }

                                    docAttr.AttributeFlags = attr.attributeflag;

                                    AGGREGATES vexAggregates = attr.aggregates;
                                    DocAttribute docAggregate = docAttr;
                                    while (vexAggregates != null)
                                    {
                                        // traverse nested aggregation (e.g. IfcStructuralLoadConfiguration)
                                        docAggregate.AggregationType = vexAggregates.aggrtype + 1;
                                        docAggregate.AggregationLower = vexAggregates.lower;
                                        docAggregate.AggregationUpper = vexAggregates.upper;
                                        docAggregate.AggregationFlag = vexAggregates.flag;

                                        vexAggregates = vexAggregates.next;
                                        if (vexAggregates != null)
                                        {
                                            // inner array (e.g. IfcStructuralLoadConfiguration)
                                            docAggregate.AggregationAttribute = new DocAttribute();
                                            docAggregate = docAggregate.AggregationAttribute;
                                        }
                                    }

                                    docAttr.Derived = attr.is_derived;

                                    if (attr.user_redeclaration != null)
                                    {
                                        docAttr.Inverse = attr.user_redeclaration;
                                    }
                                    else if (attr.is_inverse is ATTRIBUTE_DEF)
                                    {
                                        ATTRIBUTE_DEF adef = (ATTRIBUTE_DEF)attr.is_inverse;
                                        docAttr.Inverse = adef.name.text;
                                    }
                                    else if (attr.is_inverse != null)
                                    {
                                        Debug.Assert(false);
                                    }
                                }
                            }

                            foreach(DocAttribute docAttr in existingattr)
                            {
                                docEntity.Attributes.Remove(docAttr);
                                docAttr.Delete();
                            }
                        }

                        // unique rules
                        if (ent.uniquenes != null)
                        {
                            // rules are replaced, not merged (template don't apply here)
                            //docEntity.UniqueRules = new List<DocUniqueRule>();
                            foreach (UNIQUE_RULE rule in ent.uniquenes)
                            {
                                DocUniqueRule docRule = new DocUniqueRule();
                                docEntity.UniqueRules.Add(docRule);
                                docRule.Name = rule.name;

                                docRule.Items = new List<DocUniqueRuleItem>();
                                foreach (ATTRIBUTE_DEF ruleitem in rule.for_attribute)
                                {
                                    DocUniqueRuleItem item = new DocUniqueRuleItem();
                                    item.Name = ruleitem.name.text;
                                    docRule.Items.Add(item);
                                }
                            }
                        }

                        // where rules
                        if (ent.wheres != null)
                        {
                            List<DocWhereRule> existingattr = new List<DocWhereRule>();
                            foreach (DocWhereRule docWhere in docEntity.WhereRules)
                            {
                                existingattr.Add(docWhere);
                            }

                            foreach (WHERE_RULE where in ent.wheres)
                            {
                                DocWhereRule docWhere = docEntity.RegisterWhereRule(where.name);
                                docWhere.Expression = where.rule_context;

                                if(existingattr.Contains(docWhere))
                                {
                                    existingattr.Remove(docWhere);
                                }

                                if (updateDescriptions && where.comment != null && where.comment.text != null)
                                {
                                    docWhere.Documentation = where.comment.text.text;
                                }
                            }

                            foreach(DocWhereRule exist in existingattr)
                            {
                                exist.Delete();
                                docEntity.WhereRules.Remove(exist);
                            }
                        }
                    }
                }
                else if (obj is ENUMERATIONS)
                {
                    ENUMERATIONS ent = (ENUMERATIONS)obj;
                    if (ent.interfaceto == null || ent.interfaceto.theschema == null)
                    {
                        if (schemata.name.Equals("IfcConstructionMgmtDomain", StringComparison.OrdinalIgnoreCase) && ent.name.text.Equals("IfcNullStyle", StringComparison.OrdinalIgnoreCase))
                        {
                            // hack to workaround vex bug
                            Debug.Assert(true);
                        }
                        else
                        {

                            DocEnumeration docEnumeration = docSchema.RegisterType<DocEnumeration>(ent.name.text);
                            if (existing.Contains(docEnumeration))
                            {
                                existing.Remove(docEnumeration);
                            }
                            mapRefs.Add(obj, docEnumeration);

                            if (updateDescriptions && ent.comment != null && ent.comment.text != null)
                            {
                                docEnumeration.Documentation = ent.comment.text.text;
                            }

                            // determine EXPRESS-G page based on placement (required for generating hyperlinks)
                            if (ent.typelayout != null && schemata.settings != null && schemata.settings.page != null)
                            {
                                ImportVexRectangle(docEnumeration, ent.typelayout.rectangle, schemata);
                            }


                            // enumeration values are replaced, not merged (template don't apply here)
                            docEnumeration.Constants.Clear();
                            foreach (string s in ent.enums)
                            {
                                DocConstant docConstant = new DocConstant();
                                docEnumeration.Constants.Add(docConstant);
                                docConstant.Name = s;
                            }
                        }
                    }
                }
                else if (obj is DEFINED_TYPE)
                {
                    DEFINED_TYPE ent = (DEFINED_TYPE)obj;

                    if (ent.interfaceto == null || ent.interfaceto.theschema == null)
                    {
                        DocDefined docDefined = docSchema.RegisterType<DocDefined>(ent.name.text);
                        if (existing.Contains(docDefined))
                        {
                            existing.Remove(docDefined);
                        }

                        mapRefs.Add(obj, docDefined);

                        if (updateDescriptions && ent.comment != null && ent.comment.text != null)
                        {
                            docDefined.Documentation = ent.comment.text.text;
                        }

                        if (ent.layout != null)
                        {
                            ImportVexRectangle(docDefined, ent.layout.rectangle, schemata);
                        }

                        if(ent.defined.object_line_layout != null)
                        {
                            foreach(DocPoint docPoint in docDefined.DiagramLine)
                            {
                                docPoint.Delete();
                            }
                            docDefined.DiagramLine.Clear();
                            ImportVexLine(ent.defined.object_line_layout, null, docDefined.DiagramLine, null);
                        }

                        OBJECT os = (OBJECT)ent.defined.defined;
                        if (os is PAGE_REF_TO)
                        {
                            os = ((PAGE_REF_TO)os).pageref;
                        }

                        if (os is PRIMITIVE_TYPE)
                        {
                            PRIMITIVE_TYPE pt = (PRIMITIVE_TYPE)os;
                            docDefined.DefinedType = pt.name.text;

                            if (pt.constraints != 0)
                            {
                                docDefined.Length = pt.constraints;
                            }
                        }
                        else if (os is DEFINED_TYPE)
                        {
                            DEFINED_TYPE dt = (DEFINED_TYPE)os;
                            docDefined.DefinedType = dt.name.text;
                        }
                        else if (os is ENTITIES)
                        {
                            ENTITIES et = (ENTITIES)os;
                            docDefined.DefinedType = et.name.text;
                        }
                        else
                        {
                            Debug.Assert(false);
                        }

                        // aggregation
                        AGGREGATES vexAggregates = ent.defined.aggregates;
                        if (vexAggregates != null)
                        {
                            DocAttribute docAggregate = new DocAttribute();
                            docDefined.Aggregation = docAggregate;

                            docAggregate.AggregationType = vexAggregates.aggrtype + 1;
                            docAggregate.AggregationLower = vexAggregates.lower;
                            docAggregate.AggregationUpper = vexAggregates.upper;
                            docAggregate.AggregationFlag = vexAggregates.flag;
                        }

                        // where rules
                        if (ent.whererules != null)
                        {
                            // rules are replaced, not merged (template don't apply here)
                            foreach(DocWhereRule docWhere in docDefined.WhereRules)
                            {
                                docWhere.Delete();
                            }
                            docDefined.WhereRules.Clear();
                            foreach (WHERE_RULE where in ent.whererules)
                            {
                                DocWhereRule docWhere = new DocWhereRule();
                                docDefined.WhereRules.Add(docWhere);
                                docWhere.Name = where.name;
                                docWhere.Expression = where.rule_context;

                                if (where.comment != null && where.comment.text != null)
                                {
                                    docWhere.Documentation = where.comment.text.text;
                                }
                            }
                        }

                    }
                }
                else if (obj is SELECTS)
                {
                    SELECTS ent = (SELECTS)obj;
                    if (ent.interfaceto == null || ent.interfaceto.theschema == null)
                    {
                        DocSelect docSelect = docSchema.RegisterType<DocSelect>(ent.name.text);
                        if (existing.Contains(docSelect))
                        {
                            existing.Remove(docSelect);
                        }
                        mapRefs.Add(obj, docSelect);

                        if (updateDescriptions && ent.comment != null && ent.comment.text != null)
                        {
                            docSelect.Documentation = ent.comment.text.text;
                        }

                        // determine EXPRESS-G page based on placement (required for generating hyperlinks)
                        if (ent.typelayout != null)
                        {
                            ImportVexRectangle(docSelect, ent.typelayout.rectangle, schemata);
                        }

                        docSelect.Selects.Clear();
                        docSelect.Tree.Clear();
                        foreach (SELECT_DEF sdef in ent.selects)
                        {
                            DocLine docLine = new DocLine();
                            docSelect.Tree.Add(docLine);
                            ImportVexLine(sdef.layout, null, docLine.DiagramLine, null);

                            mapSL.Add(sdef, docLine);

                            if (sdef.def is TREE)
                            {
                                TREE tree = (TREE)sdef.def;

                                foreach (OBJECT o in tree.list)
                                {
                                    DocSelectItem dsi = new DocSelectItem();
                                    docSelect.Selects.Add(dsi);

                                    OBJECT os = o;
                                    if (o is SELECT_DEF)
                                    {
                                        SELECT_DEF selectdef = (SELECT_DEF)o;

                                        DocLine docLineSub = new DocLine();
                                        docLine.Tree.Add(docLineSub);
                                        ImportVexLine(selectdef.layout, null, docLineSub.DiagramLine, null);

                                        mapSL.Add(selectdef, docLineSub);

                                        os = ((SELECT_DEF)o).def;
                                    }
                                    else
                                    {
                                        Debug.Assert(false);
                                    }

                                    if (os is PAGE_REF_TO)
                                    {
                                        PAGE_REF_TO pr = (PAGE_REF_TO)os;
                                        os = pr.pageref;
                                    }

                                    if (os is DEFINITION)
                                    {
                                        dsi.Name = ((DEFINITION)os).name.text;
                                    }
                                }
                            }
                            else
                            {
                                OBJECT os = (OBJECT)sdef.def;

                                if (os is PAGE_REF_TO)
                                {
                                    PAGE_REF_TO pr = (PAGE_REF_TO)os;
                                    os = pr.pageref;
                                }

                                DocSelectItem dsi = new DocSelectItem();
                                docSelect.Selects.Add(dsi);
                                if (os is DEFINITION)
                                {
                                    dsi.Name = ((DEFINITION)os).name.text;
                                }
                            }
                        }
                    }
                }
                else if (obj is GLOBAL_RULE)
                {
                    GLOBAL_RULE func = (GLOBAL_RULE)obj;

                    DocGlobalRule docFunction = docSchema.RegisterRule(func.name);
                    if (existing.Contains(docFunction))
                    {
                        existing.Remove(docFunction);
                    }

                    // clear out existing if merging
                    docFunction.WhereRules.Clear();

                    if (updateDescriptions && func.comment != null && func.comment.text != null)
                    {
                        docFunction.Documentation = func.comment.text.text;
                    }
                    docFunction.Expression = func.rule_context;

                    foreach (WHERE_RULE wr in func.where_rule)
                    {
                        DocWhereRule docW = new DocWhereRule();
                        docW.Name = wr.name;
                        docW.Expression = wr.rule_context;
                        if (wr.comment != null)
                        {
                            docW.Documentation = wr.comment.text.text;
                        }
                        docFunction.WhereRules.Add(docW);
                    }

                    if (func.for_entities.Count == 1)
                    {
                        docFunction.ApplicableEntity = func.for_entities[0].ToString();
                    }
                }
                else if (obj is USER_FUNCTION)
                {
                    USER_FUNCTION func = (USER_FUNCTION)obj;

                    DocFunction docFunction = docSchema.RegisterFunction(func.name);
                    if (existing.Contains(docFunction))
                    {
                        existing.Remove(docFunction);
                    }

                    if (updateDescriptions && func.comment != null && func.comment.text != null)
                    {
                        docFunction.Documentation = func.comment.text.text;
                    }
                    docFunction.Expression = func.rule_context;

                    // NOTE: While the VEX schema can represent parameters and return values, Visual Express does not implement it!
                    // Rather, parameter info is also included in the 'rule_context'
                    if (func.return_value != null)
                    {
                        docFunction.ReturnValue = func.return_value.ToString();
                    }
                    else
                    {
                        docFunction.ReturnValue = null;
                    }
                    docFunction.Parameters.Clear();
                    if (func.parameter_list != null)
                    {
                        foreach (PARAMETER par in func.parameter_list)
                        {
                            DocParameter docParameter = new DocParameter();
                            docParameter.Name = par.name;
                            docParameter.DefinedType = par.parameter_type.ToString();
                            docFunction.Parameters.Add(docParameter);
                        }
                    }
                }
                else if (obj is PRIMITIVE_TYPE)
                {
                    PRIMITIVE_TYPE prim = (PRIMITIVE_TYPE)obj;

                    DocPrimitive docPrimitive = new DocPrimitive();
                    docPrimitive.Name = prim.name.text;
                    if (prim.layout != null)
                    {
                        ImportVexRectangle(docPrimitive, prim.layout.rectangle, schemata);
                    }

                    docSchema.Primitives.Add(docPrimitive);
                    mapRefs.Add(obj, docPrimitive);
                }
                else if (obj is COMMENT)
                {
                    COMMENT comment = (COMMENT)obj;

                    // only deal with comments that are part of EXPRESS-G layout -- ignore those referenced by definitions and old cruft left behind due to older versions of VisualE that were buggy
                    if (comment.layout != null)
                    {
                        DocComment docComment = new DocComment();
                        docComment.Documentation = comment.text.text;
                        ImportVexRectangle(docComment, comment.layout.rectangle, schemata);

                        docSchema.Comments.Add(docComment);
                    }
                }
                else if (obj is INTERFACE_SCHEMA)
                {
                    INTERFACE_SCHEMA iface = (INTERFACE_SCHEMA)obj;

                    DocSchemaRef docSchemaRef = new DocSchemaRef();
                    docSchema.SchemaRefs.Add(docSchemaRef);

                    docSchemaRef.Name = iface.schema_name;

                    foreach (object o in iface.item)
                    {
                        if (o is DEFINITION)
                        {
                            DocDefinitionRef docDefRef = new DocDefinitionRef();
                            docSchemaRef.Definitions.Add(docDefRef);
                            mapRefs.Add(o, docDefRef);

                            docDefRef.Name = ((DEFINITION)o).name.text;

                            if (o is DEFINED_TYPE)
                            {
                                DEFINED_TYPE dt = (DEFINED_TYPE)o;
                                if (dt.layout != null)
                                {
                                    ImportVexRectangle(docDefRef, dt.layout.rectangle, schemata);
                                }
                            }
                            else if (o is ENTITIES)
                            {
                                ENTITIES ents = (ENTITIES)o;
                                if (ents.layout != null) // null for IfcPolyline reference in IfcGeometricModelResource
                                {
                                    ImportVexRectangle(docDefRef, ents.layout.rectangle, schemata);
                                }

                                if (ents.subtypes != null)
                                {
                                    foreach (SUBTYPE_DEF subdef in ents.subtypes)
                                    {
                                        OBJECT_LINE_LAYOUT linelayout = subdef.layout;

                                        DocLine docSub = new DocLine();
                                        ImportVexLine(subdef.layout, null, docSub.DiagramLine, null);
                                        docDefRef.Tree.Add(docSub);

                                        if(subdef.the_subtype is TREE)
                                        {
                                            TREE tree = (TREE)subdef.the_subtype;
                                            foreach(object oo in tree.list)
                                            {
                                                if(oo is SUBTYPE_DEF)
                                                {
                                                    SUBTYPE_DEF subsubdef = (SUBTYPE_DEF)oo;
                                                    DocLine docSubSub = new DocLine();
                                                    docSub.Tree.Add(docSubSub);

                                                    ImportVexLine(subsubdef.layout, null, docSubSub.DiagramLine, null);

                                                    mapSubs.Add(subsubdef, docSubSub);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            else if (o is ENUMERATIONS)
                            {
                                ENUMERATIONS enums = (ENUMERATIONS)o;
                                if (enums.typelayout != null)
                                {
                                    ImportVexRectangle(docDefRef, enums.typelayout.rectangle, schemata);
                                }
                            }
                            else if (o is SELECTS)
                            {
                                SELECTS sels = (SELECTS)o;
                                if (sels.typelayout != null)
                                {
                                    ImportVexRectangle(docDefRef, sels.typelayout.rectangle, schemata);
                                }
                            }
                            else if(o is SCHEMA_REF)
                            {
                                SCHEMA_REF sref = (SCHEMA_REF)o;
                                if(sref.layout != null)
                                {
                                    ImportVexRectangle(docDefRef, sref.layout.rectangle, schemata);
                                }
                            }
                        }
                        else if (o is USER_FUNCTION)
                        {
                            DocDefinitionRef docDefRef = new DocDefinitionRef();
                            docSchemaRef.Definitions.Add(docDefRef);

                            USER_FUNCTION uf = (USER_FUNCTION)o;
                            docDefRef.Name = uf.name;
                        }
                    }
                }
                else if (obj is PAGE_REF)
                {
                    PAGE_REF pageref = (PAGE_REF)obj;

                    DocPageTarget docPageTarget = new DocPageTarget();
                    docSchema.PageTargets.Add(docPageTarget);
                    docPageTarget.Name = pageref.text.text;
                    docPageTarget.DiagramNumber = pageref.pagenr;
                    ImportVexLine(pageref.pageline.layout, null, docPageTarget.DiagramLine, null);
                    ImportVexRectangle(docPageTarget, pageref.layout.rectangle, schemata);

                    foreach (PAGE_REF_TO pagerefto in pageref.pagerefto)
                    {
                        DocPageSource docPageSource = new DocPageSource();
                        docPageTarget.Sources.Add(docPageSource);

                        docPageSource.DiagramNumber = pagerefto.pagenr;
                        docPageSource.Name = pagerefto.text.text;
                        ImportVexRectangle(docPageSource, pagerefto.layout.rectangle, schemata);

                        mapRefs.Add(pagerefto, docPageSource);
                    }

                    mapPage.Add(pageref, docPageTarget);
                }
            }

            foreach (DocObject docobj in existing)
            {
                if (docobj is DocEntity)
                {
                    docSchema.Entities.Remove((DocEntity)docobj);
                }
                else if (docobj is DocType)
                {
                    docSchema.Types.Remove((DocType)docobj);
                }
                else if (docobj is DocFunction)
                {
                    docSchema.Functions.Remove((DocFunction)docobj);
                }
                else if (docobj is DocGlobalRule)
                {
                    docSchema.GlobalRules.Remove((DocGlobalRule)docobj);
                }

                docobj.Delete();
            }

            // now fix up attributes
            foreach (ATTRIBUTE_DEF docAtt in mapAtts.Keys)
            {
                DocAttribute docAttr = mapAtts[docAtt];
                docAttr.Definition = mapRefs[docAtt.the_attribute];
            }

            foreach (PAGE_REF page in mapPage.Keys)
            {
                DocPageTarget docPage = mapPage[page];
                docPage.Definition = mapRefs[page.pageline.pageref];
            }

            foreach (SELECT_DEF sd in mapSL.Keys)
            {
                DocLine docLine = mapSL[sd];
                if (mapRefs.ContainsKey(sd.def))
                {
                    docLine.Definition = mapRefs[sd.def];
                }
            }

            foreach (SUBTYPE_DEF sd in mapSubs.Keys)
            {
                DocLine docLine = mapSubs[sd];
                if (mapRefs.ContainsKey(sd.the_subtype))
                {
                    docLine.Definition = mapRefs[sd.the_subtype];
                }
            }

            foreach(object o in mapRefs.Keys)
            {
                if (o is DEFINED_TYPE)
                {
                    DEFINED_TYPE def = (DEFINED_TYPE)o;
                    if (def.interfaceto == null || def.interfaceto.theschema == null)
                    {
                        // declared within
                        DocDefined docDef = (DocDefined)mapRefs[o];
                        docDef.Definition = mapRefs[def.defined.defined];
                    }
                }
            }

            return docSchema;
        }

        internal static void ExportIfc(IfcProject ifcProject, DocProject docProject, Dictionary<DocObject, bool> included)
        {
            ifcProject.Name = "IFC4 Property Set Templates";

            // cache enumerators
            Dictionary<string, DocPropertyEnumeration> mapEnums = new Dictionary<string,DocPropertyEnumeration>();
            foreach (DocSection docSect in docProject.Sections)
            {
                foreach (DocSchema docSchema in docSect.Schemas)
                {
                    foreach (DocPropertyEnumeration docEnum in docSchema.PropertyEnums)
                    {
                        try
                        {
                            mapEnums.Add(docEnum.Name, docEnum);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            IfcLibraryInformation ifcLibInfo = null;
#if false // optimization: don't include library info
            IfcLibraryInformation ifcLibInfo = new IfcLibraryInformation();
            ifcLibInfo.Publisher = null; // BuildingSmart...
            ifcLibInfo.Version = "IFC4";
            ifcLibInfo.VersionDate = DateTime.UtcNow;
            ifcLibInfo.Location = "";
            ifcLibInfo.Name = ifcProject.Name;
            IfcRelAssociatesLibrary ifcRelLibProject = new IfcRelAssociatesLibrary();
            ifcRelLibProject.RelatingLibrary = ifcLibInfo;
            ifcRelLibProject.RelatedObjects.Add(ifcProject);
#endif

            IfcRelDeclares rel = new IfcRelDeclares();
            rel.RelatingContext = ifcProject;

            ifcProject.Declares.Add(rel);

            foreach (DocSection docSection in docProject.Sections)
            {
                foreach (DocSchema docSchema in docSection.Schemas)
                {
                    foreach (DocPropertySet docPset in docSchema.PropertySets)
                    {
                        if (included == null || included.ContainsKey(docPset))
                        {

                            IfcPropertySetTemplate ifcPset = new IfcPropertySetTemplate();
                            rel.RelatedDefinitions.Add(ifcPset);
                            ifcPset.GlobalId = SGuid.Format(docPset.Uuid);
                            ifcPset.Name = docPset.Name;
                            ifcPset.Description = docPset.Documentation;

                            switch (docPset.PropertySetType)
                            {
                                case "PSET_TYPEDRIVENOVERRIDE":
                                    ifcPset.PredefinedType = IfcPropertySetTemplateTypeEnum.PSET_TYPEDRIVENOVERRIDE;
                                    break;

                                case "PSET_OCCURRENCEDRIVEN":
                                    ifcPset.PredefinedType = IfcPropertySetTemplateTypeEnum.PSET_OCCURRENCEDRIVEN;
                                    break;

                                case "PSET_PERFORMANCEDRIVEN":
                                    ifcPset.PredefinedType = IfcPropertySetTemplateTypeEnum.PSET_PERFORMANCEDRIVEN;
                                    break;
                            }

                            ifcPset.ApplicableEntity = docPset.ApplicableType;

                            foreach (DocLocalization docLoc in docPset.Localization)
                            {
                                IfcLibraryReference ifcLib = new IfcLibraryReference();
                                ifcLib.Language = docLoc.Locale;
                                ifcLib.Name = docLoc.Name;
                                ifcLib.Description = docLoc.Documentation;
                                ifcLib.ReferencedLibrary = ifcLibInfo;

                                IfcRelAssociatesLibrary ifcRal = new IfcRelAssociatesLibrary();
                                ifcRal.RelatingLibrary = ifcLib;
                                ifcRal.RelatedObjects.Add(ifcPset);

                                ifcPset.HasAssociations.Add(ifcRal);
                            }

                            foreach (DocProperty docProp in docPset.Properties)
                            {
                                IfcPropertyTemplate ifcProp = ExportIfcPropertyTemplate(docProp, ifcLibInfo, mapEnums);
                                ifcPset.HasPropertyTemplates.Add(ifcProp);
                            }
                        }
                    }

                    foreach (DocQuantitySet docQuantitySet in docSchema.QuantitySets)
                    {
                        if (included == null || included.ContainsKey(docQuantitySet))
                        {
                            IfcPropertySetTemplate ifcPset = new IfcPropertySetTemplate();
                            rel.RelatedDefinitions.Add(ifcPset);
                            ifcPset.Name = docQuantitySet.Name;
                            ifcPset.Description = docQuantitySet.Documentation;
                            ifcPset.PredefinedType = IfcPropertySetTemplateTypeEnum.QTO_OCCURRENCEDRIVEN;
                            ifcPset.ApplicableEntity = docQuantitySet.ApplicableType;

                            foreach (DocLocalization docLoc in docQuantitySet.Localization)
                            {
                                IfcLibraryReference ifcLib = new IfcLibraryReference();
                                ifcLib.Language = docLoc.Locale;
                                ifcLib.Name = docLoc.Name;
                                ifcLib.Description = docLoc.Documentation;
                                ifcLib.ReferencedLibrary = ifcLibInfo;

                                IfcRelAssociatesLibrary ifcRal = new IfcRelAssociatesLibrary();
                                ifcRal.RelatingLibrary = ifcLib;
                                ifcRal.RelatedObjects.Add(ifcPset);
                            }

                            foreach (DocQuantity docProp in docQuantitySet.Quantities)
                            {
                                IfcSimplePropertyTemplate ifcProp = new IfcSimplePropertyTemplate();
                                ifcPset.HasPropertyTemplates.Add(ifcProp);
                                ifcProp.Name = docProp.Name;
                                ifcProp.Description = docProp.Documentation;
                                ifcProp.TemplateType = (IfcSimplePropertyTemplateTypeEnum)Enum.Parse(typeof(IfcSimplePropertyTemplateTypeEnum), docProp.QuantityType.ToString());

                                foreach (DocLocalization docLoc in docProp.Localization)
                                {
                                    IfcLibraryReference ifcLib = new IfcLibraryReference();
                                    ifcLib.Language = docLoc.Locale;
                                    ifcLib.Name = docLoc.Name;
                                    ifcLib.Description = docLoc.Documentation;
                                    ifcLib.ReferencedLibrary = ifcLibInfo;

                                    IfcRelAssociatesLibrary ifcRal = new IfcRelAssociatesLibrary();
                                    ifcRal.RelatingLibrary = ifcLib;
                                    ifcRal.RelatedObjects.Add(ifcProp);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static IfcPropertyTemplate ExportIfcPropertyTemplate(DocProperty docProp, IfcLibraryInformation ifcLibInfo, Dictionary<string, DocPropertyEnumeration> mapEnums)
        {
            if (docProp.PropertyType == DocPropertyTemplateTypeEnum.COMPLEX)
            {
                IfcComplexPropertyTemplate ifcProp = new IfcComplexPropertyTemplate();
                ifcProp.GlobalId = SGuid.Format(docProp.Uuid);
                ifcProp.Name = docProp.Name;
                ifcProp.Description = docProp.Documentation;
                ifcProp.TemplateType = IfcComplexPropertyTemplateTypeEnum.P_COMPLEX;
                ifcProp.UsageName = docProp.PrimaryDataType;

                foreach (DocProperty docSubProp in docProp.Elements)
                {
                    IfcPropertyTemplate ifcSub = ExportIfcPropertyTemplate(docSubProp, ifcLibInfo, mapEnums);
                    ifcProp.HasPropertyTemplates.Add(ifcSub);
                }

                return ifcProp;
            }
            else
            {
                IfcSimplePropertyTemplate ifcProp = new IfcSimplePropertyTemplate();
                ifcProp.GlobalId = SGuid.Format(docProp.Uuid);
                ifcProp.Name = docProp.Name;
                ifcProp.Description = docProp.Documentation;
                ifcProp.TemplateType = (IfcSimplePropertyTemplateTypeEnum)Enum.Parse(typeof(IfcSimplePropertyTemplateTypeEnum), docProp.PropertyType.ToString());
                ifcProp.PrimaryMeasureType = docProp.PrimaryDataType;
                ifcProp.SecondaryMeasureType = docProp.SecondaryDataType;

                foreach (DocLocalization docLoc in docProp.Localization)
                {
                    IfcLibraryReference ifcLib = new IfcLibraryReference();
                    ifcLib.Language = docLoc.Locale;
                    ifcLib.Name = docLoc.Name;
                    ifcLib.Description = docLoc.Documentation;
                    ifcLib.ReferencedLibrary = ifcLibInfo;

                    IfcRelAssociatesLibrary ifcRal = new IfcRelAssociatesLibrary();
                    ifcRal.RelatingLibrary = ifcLib;
                    ifcRal.RelatedObjects.Add(ifcProp);

                    ifcProp.HasAssociations.Add(ifcRal);
                }


                // enumerations
                if (ifcProp.TemplateType == IfcSimplePropertyTemplateTypeEnum.P_ENUMERATEDVALUE && ifcProp.SecondaryMeasureType != null)
                {
                    // NEW: lookup formal enumeration
                    string propdatatype = ifcProp.SecondaryMeasureType;
                    int colon = propdatatype.IndexOf(':');
                    if(colon > 0)
                    {
                        propdatatype = propdatatype.Substring(0, colon);
                    }

                    DocPropertyEnumeration docEnumeration = null;
                    if(mapEnums.TryGetValue(propdatatype, out docEnumeration))
                    {
                        ifcProp.Enumerators = new IfcPropertyEnumeration();
                        ifcProp.GlobalId = SGuid.Format(docEnumeration.Uuid);
                        ifcProp.Enumerators.Name = docEnumeration.Name;
                        ifcProp.Enumerators.EnumerationValues = new List<IfcLabel>();
                        ifcProp.SecondaryMeasureType = null;

                        foreach(DocPropertyConstant docConst in docEnumeration.Constants)
                        {
                            IfcLabel label = new IfcLabel();
                            label.Value = docConst.Name;
                            ifcProp.Enumerators.EnumerationValues.Add(label);


                            foreach (DocLocalization docLoc in docConst.Localization)
                            {
                                IfcLibraryReference ifcLib = new IfcLibraryReference();
                                ifcLib.Identification = docConst.Name; // distinguishes the constant value within the enumeration
                                ifcLib.Language = docLoc.Locale;
                                ifcLib.Name = docLoc.Name;
                                ifcLib.Description = docLoc.Documentation;
                                ifcLib.ReferencedLibrary = ifcLibInfo;

                                if(docLoc.Documentation == null && docLoc.Locale == "en")
                                {
                                    ifcLib.Description = docConst.Documentation;
                                }

                                IfcRelAssociatesLibrary ifcRal = new IfcRelAssociatesLibrary();
                                ifcRal.RelatingLibrary = ifcLib;
                                ifcRal.RelatedObjects.Add(ifcProp);

                                ifcProp.HasAssociations.Add(ifcRal);
                            }

                        }
                    }

                }

                return ifcProp;
            }
        }

        public static QtoSetDef ExportQto(DocQuantitySet docPset)
        {
            string[] apptypes = new string[0];
            if (docPset.ApplicableType != null)
            {
                apptypes = docPset.ApplicableType.Split(',');
            }

            // convert to psd schema
            QtoSetDef psd = new QtoSetDef();
            psd.Name = docPset.Name;
            psd.Definition = docPset.Documentation;
            psd.Versions = new List<IfcVersion>();
            psd.Versions.Add(new IfcVersion());
            psd.Versions[0].version = "IFC4";
            psd.ApplicableTypeValue = docPset.ApplicableType;
            psd.ApplicableClasses = new List<ClassName>();
            foreach (string app in apptypes)
            {
                ClassName cln = new ClassName();
                cln.Value = app;
                psd.ApplicableClasses.Add(cln);
            }

            psd.QtoDefinitionAliases = new List<QtoDefinitionAlias>();
            foreach (DocLocalization docLocal in docPset.Localization)
            {
                QtoDefinitionAlias alias = new QtoDefinitionAlias();
                psd.QtoDefinitionAliases.Add(alias);
                alias.lang = docLocal.Locale;
                alias.Value = docLocal.Documentation;
            }

            psd.QtoDefs = new List<QtoDef>();
            foreach (DocQuantity docProp in docPset.Quantities)
            {
                QtoDef prop = new QtoDef();
                psd.QtoDefs.Add(prop);
                prop.Name = docProp.Name;
                prop.Definition = docProp.Documentation;

                prop.NameAliases = new List<NameAlias>();
                prop.DefinitionAliases = new List<DefinitionAlias>();
                foreach (DocLocalization docLocal in docProp.Localization)
                {
                    NameAlias na = new NameAlias();
                    prop.NameAliases.Add(na);
                    na.lang = docLocal.Locale;
                    na.Value = docLocal.Name;

                    DefinitionAlias da = new DefinitionAlias();
                    prop.DefinitionAliases.Add(da);
                    da.lang = docLocal.Locale;
                    da.Value = docLocal.Documentation;
                }

                prop.QtoType = docProp.QuantityType.ToString();
            }

            return psd;
        }

        public static PropertySetDef ExportPsd(DocPropertySet docPset, Dictionary<string, DocPropertyEnumeration> mapEnum)
        {
            string[] apptypes = new string[0];

            if (docPset.ApplicableType != null)
            {
                apptypes = docPset.ApplicableType.Split(',');
            }

            // convert to psd schema
            PropertySetDef psd = new PropertySetDef();
            psd.Versions = new List<IfcVersion>();
            psd.Versions.Add(new IfcVersion());
            psd.Versions[0].version = "IFC4";
            psd.Name = docPset.Name;
            psd.Definition = docPset.Documentation;
            psd.TemplateType = docPset.PropertySetType;
            psd.ApplicableTypeValue = docPset.ApplicableType;
            psd.ApplicableClasses = new List<ClassName>();
            foreach (string app in apptypes)
            {
                ClassName cln = new ClassName();
                cln.Value = app;
                psd.ApplicableClasses.Add(cln);
            }
            psd.IfdGuid = docPset.Uuid.ToString("N");

            psd.PsetDefinitionAliases = new List<PsetDefinitionAlias>();
            foreach (DocLocalization docLocal in docPset.Localization)
            {
                PsetDefinitionAlias alias = new PsetDefinitionAlias();
                psd.PsetDefinitionAliases.Add(alias);
                alias.lang = docLocal.Locale;
                alias.Value = docLocal.Documentation;
            }

            psd.PropertyDefs = new List<PropertyDef>();
            foreach (DocProperty docProp in docPset.Properties)
            {
                PropertyDef prop = new PropertyDef();
                psd.PropertyDefs.Add(prop);
                ExportPsdProperty(docProp, prop, mapEnum);
            }

            return psd;
        }

        private static void ExportPsdProperty(DocProperty docProp, PropertyDef prop, Dictionary<string, DocPropertyEnumeration> mapPropEnum)
        {
            prop.IfdGuid = docProp.Uuid.ToString("N");
            prop.Name = docProp.Name;
            prop.Definition = docProp.Documentation;

            prop.NameAliases = new List<NameAlias>();
            prop.DefinitionAliases = new List<DefinitionAlias>();
            foreach (DocLocalization docLocal in docProp.Localization)
            {
                NameAlias na = new NameAlias();
                prop.NameAliases.Add(na);
                na.lang = docLocal.Locale;
                na.Value = docLocal.Name;

                DefinitionAlias da = new DefinitionAlias();
                prop.DefinitionAliases.Add(da);
                da.lang = docLocal.Locale;
                da.Value = docLocal.Documentation;
            }

            prop.PropertyType = new PropertyType();
            switch (docProp.PropertyType)
            {
                case DocPropertyTemplateTypeEnum.P_SINGLEVALUE:
                    prop.PropertyType.TypePropertySingleValue = new TypePropertySingleValue();
                    prop.PropertyType.TypePropertySingleValue.DataType = new DataType();
                    prop.PropertyType.TypePropertySingleValue.DataType.type = docProp.PrimaryDataType;
                    break;

                case DocPropertyTemplateTypeEnum.P_BOUNDEDVALUE:
                    prop.PropertyType.TypePropertyBoundedValue = new TypePropertyBoundedValue();
                    prop.PropertyType.TypePropertyBoundedValue.DataType = new DataType();
                    prop.PropertyType.TypePropertyBoundedValue.DataType.type = docProp.PrimaryDataType;
                    break;

                case DocPropertyTemplateTypeEnum.P_ENUMERATEDVALUE:
                    prop.PropertyType.TypePropertyEnumeratedValue = new TypePropertyEnumeratedValue();
                    prop.PropertyType.TypePropertyEnumeratedValue.EnumList = new EnumList();
                    {
                        if (docProp.SecondaryDataType != null)
                        {
                            string[] parts = docProp.SecondaryDataType.Split(':');
                            if (parts.Length == 2)
                            {
                                string[] enums = parts[1].Split(',');
                                prop.PropertyType.TypePropertyEnumeratedValue.EnumList.name = parts[0];
                                prop.PropertyType.TypePropertyEnumeratedValue.EnumList.Items = new List<EnumItem>();
                                foreach (string eachenum in enums)
                                {
                                    EnumItem eni = new EnumItem();
                                    prop.PropertyType.TypePropertyEnumeratedValue.EnumList.Items.Add(eni);
                                    eni.Value = eachenum.Trim();
                                }
                            }

                            string propenum = docProp.SecondaryDataType;
                            if (propenum != null)
                            {
                                int colon = propenum.IndexOf(':');
                                if (colon > 0)
                                {
                                    propenum = propenum.Substring(0, colon);
                                }
                            }
                            DocPropertyEnumeration docPropEnum = null;
                            if (mapPropEnum.TryGetValue(propenum, out docPropEnum))
                            {
                                prop.PropertyType.TypePropertyEnumeratedValue.ConstantList = new ConstantList();
                                prop.PropertyType.TypePropertyEnumeratedValue.ConstantList.Items = new List<ConstantDef>();
                                foreach (DocPropertyConstant docPropConst in docPropEnum.Constants)
                                {
                                    ConstantDef con = new ConstantDef();
                                    prop.PropertyType.TypePropertyEnumeratedValue.ConstantList.Items.Add(con);

                                    con.Name = docPropConst.Name.Trim();
                                    con.Definition = docPropConst.Documentation;
                                    con.NameAliases = new List<NameAlias>();
                                    con.DefinitionAliases = new List<DefinitionAlias>();

                                    foreach (DocLocalization docLocal in docPropConst.Localization)
                                    {
                                        NameAlias na = new NameAlias();
                                        con.NameAliases.Add(na);
                                        na.lang = docLocal.Locale;
                                        na.Value = docLocal.Name.Trim();

                                        DefinitionAlias da = new DefinitionAlias();
                                        con.DefinitionAliases.Add(da);
                                        da.lang = docLocal.Locale;
                                        da.Value = docLocal.Documentation;
                                    }
                                }
                            }
                        }
                    }

                    break;

                case DocPropertyTemplateTypeEnum.P_LISTVALUE:
                    prop.PropertyType.TypePropertyListValue = new TypePropertyListValue();
                    prop.PropertyType.TypePropertyListValue.ListValue = new ListValue();
                    prop.PropertyType.TypePropertyListValue.ListValue.DataType = new DataType();
                    prop.PropertyType.TypePropertyListValue.ListValue.DataType.type = docProp.PrimaryDataType;
                    break;

                case DocPropertyTemplateTypeEnum.P_TABLEVALUE:
                    prop.PropertyType.TypePropertyTableValue = new TypePropertyTableValue();
                    prop.PropertyType.TypePropertyTableValue.Expression = String.Empty;
                    prop.PropertyType.TypePropertyTableValue.DefiningValue = new DefiningValue();
                    prop.PropertyType.TypePropertyTableValue.DefiningValue.DataType = new DataType();
                    prop.PropertyType.TypePropertyTableValue.DefiningValue.DataType.type = docProp.PrimaryDataType;
                    prop.PropertyType.TypePropertyTableValue.DefinedValue = new DefinedValue();
                    prop.PropertyType.TypePropertyTableValue.DefinedValue.DataType = new DataType();
                    prop.PropertyType.TypePropertyTableValue.DefinedValue.DataType.type = docProp.SecondaryDataType;
                    break;

                case DocPropertyTemplateTypeEnum.P_REFERENCEVALUE:
                    prop.PropertyType.TypePropertyReferenceValue = new TypePropertyReferenceValue();
                    prop.PropertyType.TypePropertyReferenceValue.reftype = docProp.PrimaryDataType;
                    break;

                case DocPropertyTemplateTypeEnum.COMPLEX:
                    prop.PropertyType.TypeComplexProperty = new TypeComplexProperty();
                    prop.PropertyType.TypeComplexProperty.name = docProp.PrimaryDataType;
                    prop.PropertyType.TypeComplexProperty.PropertyDefs = new List<PropertyDef>();
                    foreach(DocProperty docSub in docProp.Elements)
                    {
                        PropertyDef sub = new PropertyDef();
                        prop.PropertyType.TypeComplexProperty.PropertyDefs.Add(sub);
                        ExportPsdProperty(docSub, sub, mapPropEnum);
                    }
                    break;
            }

        }

        public static void ImportPsdPropertyTemplate(PropertyDef def, DocProperty docprop)
        {
            DocPropertyTemplateTypeEnum proptype = DocPropertyTemplateTypeEnum.P_SINGLEVALUE;
            string datatype = String.Empty;
            string elemtype = String.Empty;

            if (def.PropertyType.TypePropertyEnumeratedValue != null)
            {
                proptype = DocPropertyTemplateTypeEnum.P_ENUMERATEDVALUE;
                datatype = "IfcLabel";
                StringBuilder sbEnum = new StringBuilder();
                sbEnum.Append(def.PropertyType.TypePropertyEnumeratedValue.EnumList.name);
                sbEnum.Append(":");
                foreach (EnumItem item in def.PropertyType.TypePropertyEnumeratedValue.EnumList.Items)
                {
                    sbEnum.Append(item.Value);
                    sbEnum.Append(",");
                }
                sbEnum.Length--; // remove trailing ','
                elemtype = sbEnum.ToString();
            }
            else if (def.PropertyType.TypePropertyReferenceValue != null)
            {
                proptype = DocPropertyTemplateTypeEnum.P_REFERENCEVALUE;
                datatype = def.PropertyType.TypePropertyReferenceValue.reftype;
                if (def.PropertyType.TypePropertyReferenceValue.DataType != null)
                {
                    elemtype = def.PropertyType.TypePropertyReferenceValue.DataType.type; // e.g. IfcTimeSeries
                }
            }
            else if (def.PropertyType.TypePropertyBoundedValue != null)
            {
                proptype = DocPropertyTemplateTypeEnum.P_BOUNDEDVALUE;
                datatype = def.PropertyType.TypePropertyBoundedValue.DataType.type;
            }
            else if (def.PropertyType.TypePropertyListValue != null)
            {
                proptype = DocPropertyTemplateTypeEnum.P_LISTVALUE;
                datatype = def.PropertyType.TypePropertyListValue.ListValue.DataType.type;
            }
            else if (def.PropertyType.TypePropertyTableValue != null)
            {
                proptype = DocPropertyTemplateTypeEnum.P_TABLEVALUE;
                datatype = def.PropertyType.TypePropertyTableValue.DefiningValue.DataType.type;
                elemtype = def.PropertyType.TypePropertyTableValue.DefinedValue.DataType.type;
            }
            else if (def.PropertyType.TypePropertySingleValue != null)
            {
                proptype = DocPropertyTemplateTypeEnum.P_SINGLEVALUE;
                datatype = def.PropertyType.TypePropertySingleValue.DataType.type;
            }
            else if (def.PropertyType.TypeComplexProperty != null)
            {
                proptype = DocPropertyTemplateTypeEnum.COMPLEX;
                datatype = def.PropertyType.TypeComplexProperty.name;
            }

            if (!String.IsNullOrEmpty(def.IfdGuid))
            {
                try
                {
                    docprop.Uuid = new Guid(def.IfdGuid);
                }
                catch
                {
                }
            }

            docprop.Name = def.Name;
            docprop.Documentation = def.Definition.Trim();
            docprop.PropertyType = proptype;
            docprop.PrimaryDataType = datatype;
            docprop.SecondaryDataType = elemtype.Trim();

            foreach (NameAlias namealias in def.NameAliases)
            {
                string localdesc = null;
                foreach (DefinitionAlias docalias in def.DefinitionAliases)
                {
                    if (docalias.lang.Equals(namealias.lang))
                    {
                        localdesc = docalias.Value;
                    }
                }

                docprop.RegisterLocalization(namealias.lang, namealias.Value, localdesc);
            }

            // recurse for complex properties
            if (def.PropertyType.TypeComplexProperty != null)
            {
                foreach (PropertyDef subdef in def.PropertyType.TypeComplexProperty.PropertyDefs)
                {
                    DocProperty subprop = docprop.RegisterProperty(subdef.Name);
                    ImportPsdPropertyTemplate(subdef, subprop);
                }
            }
        }

#if false
        internal static void ImportMvdCardinality(DocModelRule docRule, CardinalityType cardinality)
        {
            switch (cardinality)
            {
                case CardinalityType._asSchema:
                    docRule.CardinalityMin = 0;
                    docRule.CardinalityMax = 0; // same as unitialized file
                    break;

                case CardinalityType.Zero:
                    docRule.CardinalityMin = -1; // means 0:0
                    docRule.CardinalityMax = -1;
                    break;

                case CardinalityType.ZeroToOne:
                    docRule.CardinalityMin = 0;
                    docRule.CardinalityMax = 1;
                    break;

                case CardinalityType.One:
                    docRule.CardinalityMin = 1;
                    docRule.CardinalityMax = 1;
                    break;

                case CardinalityType.OneToMany:
                    docRule.CardinalityMin = 1;
                    docRule.CardinalityMax = -1;
                    break;
            }
        }
#endif

        internal static DocModelRule ImportMvdRule(AttributeRule mvdRule, Dictionary<EntityRule, DocModelRuleEntity> fixups)
        {
            DocModelRuleAttribute docRule = new DocModelRuleAttribute();
            docRule.Name = mvdRule.AttributeName;
            docRule.Description = mvdRule.Description;
            docRule.Identification = mvdRule.RuleID;
            //ImportMvdCardinality(docRule, mvdRule.Cardinality);

            if (mvdRule.EntityRules != null)
            {
                foreach (EntityRule mvdEntityRule in mvdRule.EntityRules)
                {
                    DocModelRuleEntity docRuleEntity = new DocModelRuleEntity();
                    docRuleEntity.Name = mvdEntityRule.EntityName;
                    docRuleEntity.Description = mvdEntityRule.Description;
                    docRuleEntity.Identification = mvdEntityRule.RuleID;
                    //ImportMvdCardinality(docRule, mvdRule.Cardinality);
                    docRule.Rules.Add(docRuleEntity);

                    if (mvdEntityRule.AttributeRules != null)
                    {
                        foreach (AttributeRule mvdAttributeRule in mvdEntityRule.AttributeRules)
                        {
                            DocModelRule docRuleAttribute = ImportMvdRule(mvdAttributeRule, fixups);
                            docRuleEntity.Rules.Add(docRuleAttribute);
                        }
                    }

                    if (mvdEntityRule.Constraints != null)
                    {
                        foreach (Constraint mvdConstraint in mvdEntityRule.Constraints)
                        {
                            DocModelRuleConstraint docRuleConstraint = new DocModelRuleConstraint();
                            docRuleConstraint.Description = mvdConstraint.Expression;
                            docRuleEntity.Rules.Add(docRuleConstraint);
                        }
                    }

                    if(mvdEntityRule.References != null)
                    {
                        // add it later, as referenced templates may not necessarily be loaded yet
                        fixups.Add(mvdEntityRule, docRuleEntity);
                    }
                }
            }

            if (mvdRule.Constraints != null)
            {
                foreach (Constraint mvdConstraint in mvdRule.Constraints)
                {
                    DocModelRuleConstraint docRuleConstraint = new DocModelRuleConstraint();
                    docRuleConstraint.Description = mvdConstraint.Expression;
                    docRule.Rules.Add(docRuleConstraint);
                }
            }

            return docRule;
        }

        internal static void ImportMvd(mvdXML mvd, DocProject docProject, string filepath)
        {
            if (mvd.Templates != null)
            {
                Dictionary<EntityRule, DocModelRuleEntity> fixups = new Dictionary<EntityRule, DocModelRuleEntity>();
                foreach (ConceptTemplate mvdTemplate in mvd.Templates)
                {
                    DocTemplateDefinition docDef = docProject.GetTemplate(mvdTemplate.Uuid);
                    if (docDef == null)
                    {
                        docDef = new DocTemplateDefinition();
                        docProject.Templates.Add(docDef);
                    }

                    ImportMvdTemplate(mvdTemplate, docDef, fixups);
                }

                foreach(EntityRule er in fixups.Keys)
                {
                    DocModelRuleEntity docEntityRule = fixups[er];
                    if(er.References != null)
                    {
                        foreach(TemplateRef tr in er.References)
                        {
                            DocTemplateDefinition dtd = docProject.GetTemplate(tr.Ref);
                            if(dtd != null)
                            {
                                docEntityRule.References.Add(dtd);
                            }
                        }
                    }
                }
            }

            if (mvd.Views != null)
            {
                foreach (ModelView mvdView in mvd.Views)
                {
                    DocModelView docView = docProject.GetView(mvdView.Uuid);
                    if (docView == null)
                    {
                        docView = new DocModelView();
                        docProject.ModelViews.Add(docView);
                    }

                    ImportMvdObject(mvdView, docView);

                    docView.BaseView = mvdView.BaseView;
                    docView.Exchanges.Clear();
                    Dictionary<Guid, ExchangeRequirement> mapExchange = new Dictionary<Guid, ExchangeRequirement>();
                    foreach (ExchangeRequirement mvdExchange in mvdView.ExchangeRequirements)
                    {
                        mapExchange.Add(mvdExchange.Uuid, mvdExchange);

                        DocExchangeDefinition docExchange = new DocExchangeDefinition();
                        ImportMvdObject(mvdExchange, docExchange);
                        docView.Exchanges.Add(docExchange);

                        docExchange.Applicability = (DocExchangeApplicabilityEnum)mvdExchange.Applicability;

                        // attempt to find icons if exists -- remove extention
                        try
                        {
                            string iconpath = filepath.Substring(0, filepath.Length - 7) + @"\mvd-" + docExchange.Name.ToLower().Replace(' ', '-') + ".png";
                            if (System.IO.File.Exists(iconpath))
                            {
                                docExchange.Icon = System.IO.File.ReadAllBytes(iconpath);
                            }
                        }
                        catch
                        {

                        }
                    }

                    foreach (ConceptRoot mvdRoot in mvdView.Roots)
                    {
                        // find the entity
                        DocEntity docEntity = LookupEntity(docProject, mvdRoot.ApplicableRootEntity);
                        if (docEntity != null)
                        {
                            DocConceptRoot docConceptRoot = docView.GetConceptRoot(mvdRoot.Uuid);
                            if (docConceptRoot == null)
                            {
                                docConceptRoot = new DocConceptRoot();
                                if (docView.ConceptRoots == null)
                                {
                                    docView.ConceptRoots = new List<DocConceptRoot>();
                                }
                                docView.ConceptRoots.Add(docConceptRoot);
                            }

                            ImportMvdObject(mvdRoot, docConceptRoot);
                            docConceptRoot.ApplicableEntity = docEntity;

                            if (mvdRoot.Applicability != null)
                            {
                                docConceptRoot.ApplicableTemplate = docProject.GetTemplate(mvdRoot.Applicability.Template.Ref);
                                if(mvdRoot.Applicability.TemplateRules != null)
                                {
                                    docConceptRoot.ApplicableOperator = (DocTemplateOperator)Enum.Parse(typeof(TemplateOperator), mvdRoot.Applicability.TemplateRules.Operator.ToString());
                                    foreach (TemplateRule r in mvdRoot.Applicability.TemplateRules.TemplateRule)
                                    {
                                        DocTemplateItem docItem = ImportMvdItem(r, docProject, mapExchange);
                                        docConceptRoot.ApplicableItems.Add(docItem);
                                    }
                                }
                            }

                            docConceptRoot.Concepts.Clear();
                            foreach (Concept mvdNode in mvdRoot.Concepts)
                            {
                                DocTemplateUsage docUse = new DocTemplateUsage();
                                docConceptRoot.Concepts.Add(docUse);
                                ImportMvdConcept(mvdNode, docUse, docProject, mapExchange);
                            }
                        }
                        else
                        {
                            //TODO: log error
                        }
                    }
                }
            }
        }

        private static void ImportMvdConcept(Concept mvdNode, DocTemplateUsage docUse, DocProject docProject, Dictionary<Guid, ExchangeRequirement> mapExchange)
        {
            // special case for attributes                            
            DocTemplateDefinition docTemplateDef = docProject.GetTemplate(mvdNode.Template.Ref);
            if (docTemplateDef != null)
            {
                // lookup use definition
                docUse.Uuid = mvdNode.Uuid;
                docUse.Name = mvdNode.Name;
                docUse.Definition = docTemplateDef;
                docUse.Override = mvdNode.Override;

                // content
                if (mvdNode.Definitions != null)
                {
                    foreach (Definition mvdDef in mvdNode.Definitions)
                    {
                        if (mvdDef.Body != null)
                        {
                            docUse.Documentation = mvdDef.Body.Content;
                        }
                    }
                }

                // exchange requirements
                foreach (ConceptRequirement mvdReq in mvdNode.Requirements)
                {
                    ExchangeRequirement mvdExchange = null;
                    if (mapExchange.TryGetValue(mvdReq.ExchangeRequirement, out mvdExchange))
                    {
                        DocExchangeItem docReq = new DocExchangeItem();
                        docUse.Exchanges.Add(docReq);

                        foreach (DocModelView docModel in docProject.ModelViews)
                        {
                            foreach (DocExchangeDefinition docAnno in docModel.Exchanges)
                            {
                                if (docAnno.Uuid.Equals(mvdReq.ExchangeRequirement))
                                {
                                    docReq.Exchange = docAnno;
                                    break;
                                }
                            }
                        }

                        docReq.Applicability = (DocExchangeApplicabilityEnum)mvdReq.Applicability;
                        docReq.Requirement = (DocExchangeRequirementEnum)mvdReq.Requirement;
                    }
                }

                // rules as template items
                if (mvdNode.TemplateRules != null)
                {
                    docUse.Operator = (DocTemplateOperator)Enum.Parse(typeof(DocTemplateOperator), mvdNode.TemplateRules.Operator.ToString());
                    foreach (TemplateRule rule in mvdNode.TemplateRules.TemplateRule)
                    {
                        DocTemplateItem docItem = ImportMvdItem(rule, docProject, mapExchange);
                        docUse.Items.Add(docItem);
                    }

                    if (mvdNode.TemplateRules.InnerRules != null)
                    {
                        mvdNode.ToString();
                    }
                }

            }
        }

        private static DocTemplateItem ImportMvdItem(TemplateRule rule, DocProject docProject, Dictionary<Guid, ExchangeRequirement> mapExchange)
        {
            DocTemplateItem docItem = new DocTemplateItem();
            docItem.Documentation = rule.Description;
            docItem.RuleInstanceID = rule.RuleID;
            docItem.RuleParameters = rule.Parameters;

            if (rule.References != null)
            {
                foreach (Concept con in rule.References)
                {
                    DocTemplateUsage docInner = new DocTemplateUsage();
                    docItem.Concepts.Add(docInner);
                    ImportMvdConcept(con, docInner, docProject, mapExchange);
                }
            }

            return docItem;
        }

        private static void ImportMvdTemplate(ConceptTemplate mvdTemplate, DocTemplateDefinition docDef, Dictionary<EntityRule, DocModelRuleEntity> fixups)
        {
            ImportMvdObject(mvdTemplate, docDef);
            docDef.Type = mvdTemplate.ApplicableEntity;

            docDef.Rules.Clear();
            if (mvdTemplate.Rules != null)
            {
                foreach (AttributeRule mvdRule in mvdTemplate.Rules)
                {
                    DocModelRule docRule = ImportMvdRule(mvdRule, fixups);
                    docDef.Rules.Add(docRule);
                }
            }

            // recurse through subtemplates
            if (mvdTemplate.SubTemplates != null)
            {
                foreach (ConceptTemplate mvdSub in mvdTemplate.SubTemplates)
                {
                    DocTemplateDefinition docSub = docDef.GetTemplate(mvdSub.Uuid);
                    if (docSub == null)
                    {
                        docSub = new DocTemplateDefinition();
                        docDef.Templates.Add(docSub);
                    }
                    ImportMvdTemplate(mvdSub, docSub, fixups);
                }
            }
        }

        private static DocEntity LookupEntity(DocProject project, string name)
        {
            // inefficient, but keeps code organized

            foreach (DocSection section in project.Sections)
            {
                foreach (DocSchema schema in section.Schemas)
                {
                    foreach (DocEntity entity in schema.Entities)
                    {
                        if (entity.Name.Equals(name))
                        {
                            return entity;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Helper function to populate attributes
        /// </summary>
        /// <param name="mvd"></param>
        /// <param name="doc"></param>
        private static void ImportMvdObject(Element mvd, DocObject doc)
        {
            doc.Name = mvd.Name;
            doc.Uuid = mvd.Uuid;
            doc.Version = mvd.Version;
            doc.Owner = mvd.Owner;
            doc.Status = mvd.Status;
            doc.Copyright = mvd.Copyright;
            doc.Code = mvd.Code;
            doc.Author = mvd.Author;

            if (mvd.Definitions != null)
            {
                foreach (Definition def in mvd.Definitions)
                {
                    if (def != null)
                    {
                        // base definition
                        if (def.Body != null)
                        {
                            doc.Documentation = def.Body.Content;
                        }

                        if (def.Links != null)
                        {
                            foreach (Link link in def.Links)
                            {
                                DocLocalization loc = new DocLocalization();
                                doc.Localization.Add(loc);
                                loc.Name = link.Title;
                                loc.Documentation = link.Content;
                                loc.Category = (DocCategoryEnum)Enum.Parse(typeof(DocCategoryEnum), link.Category.ToString());
                                loc.Locale = link.Lang;
                                loc.URL = link.Href;
                            }
                        }
                    }
                }
            }
        }

        internal static bool CheckFilter(Array array, object target)
        {
            if (array == null)
                return true;

            foreach (Object o in array)
            {
                if (o == target)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Expands list to include any inherited templates
        /// </summary>
        /// <param name="template"></param>
        /// <param name="list"></param>
        internal static void ExpandTemplateInheritance(DocTemplateDefinition template, List<DocTemplateDefinition> list)
        {
            if (template.Templates != null)
            {
                foreach (DocTemplateDefinition sub in template.Templates)
                {
                    ExpandTemplateInheritance(sub, list);

                    if (list.Contains(sub) && !list.Contains(template))
                    {
                        list.Add(template);
                    }
                }
            }
        }

        private static void ExportMvdConcept(Concept mvdConceptLeaf, DocTemplateUsage docTemplateUsage)
        {
            if (docTemplateUsage.Definition == null)
                return;

            ExportMvdObject(mvdConceptLeaf, docTemplateUsage);

            mvdConceptLeaf.Template = new TemplateRef();
            mvdConceptLeaf.Template.Ref = docTemplateUsage.Definition.Uuid;

            mvdConceptLeaf.Override = docTemplateUsage.Override;

            // requirements
            foreach (DocExchangeItem docExchangeRef in docTemplateUsage.Exchanges)
            {
                if (docExchangeRef.Exchange != null)
                {
                    ConceptRequirement mvdRequirement = new ConceptRequirement();
                    mvdConceptLeaf.Requirements.Add(mvdRequirement);
                    switch (docExchangeRef.Applicability)
                    {
                        case DocExchangeApplicabilityEnum.Export:
                            mvdRequirement.Applicability = ApplicabilityEnum.Export;
                            break;

                        case DocExchangeApplicabilityEnum.Import:
                            mvdRequirement.Applicability = ApplicabilityEnum.Import;
                            break;
                    }

                    switch (docExchangeRef.Requirement)
                    {
                        case DocExchangeRequirementEnum.Excluded:
                            mvdRequirement.Requirement = RequirementEnum.Excluded;
                            break;

                        case DocExchangeRequirementEnum.Mandatory:
                            mvdRequirement.Requirement = RequirementEnum.Mandatory;
                            break;

                        case DocExchangeRequirementEnum.NotRelevant:
                            mvdRequirement.Requirement = RequirementEnum.NotRelevant;
                            break;

                        case DocExchangeRequirementEnum.Optional:
                            mvdRequirement.Requirement = RequirementEnum.Optional;
                            break;

                        default:
                            mvdRequirement.Requirement = RequirementEnum.Optional;
                            break;
                    }

                    mvdRequirement.ExchangeRequirement = docExchangeRef.Exchange.Uuid;
                }
            }

            // rules                                    
            mvdConceptLeaf.TemplateRules = new TemplateRules();
            mvdConceptLeaf.TemplateRules.Operator = (TemplateOperator)Enum.Parse(typeof(TemplateOperator), docTemplateUsage.Operator.ToString());
            foreach (DocTemplateItem docRule in docTemplateUsage.Items)
            {
                TemplateRule mvdTemplateRule = ExportMvdItem(docRule);
                mvdConceptLeaf.TemplateRules.TemplateRule.Add(mvdTemplateRule);

                // using proposed mvdXML schema
                if (docRule.Concepts.Count > 0)
                {
                    mvdConceptLeaf.SubConcepts = new List<Concept>();
                    mvdTemplateRule.References = new List<Concept>();
                    foreach (DocTemplateUsage docInner in docRule.Concepts)
                    {
                        Concept mvdInner = new Concept();
                        mvdTemplateRule.References.Add(mvdInner);
                        ExportMvdConcept(mvdInner, docInner);
                    }
                }
            }
        }

        internal static TemplateRule ExportMvdItem(DocTemplateItem docRule)
        {
            TemplateRule mvdTemplateRule = new TemplateRule();
            mvdTemplateRule.RuleID = docRule.RuleInstanceID;
            mvdTemplateRule.Description = docRule.Documentation;
            mvdTemplateRule.Parameters = docRule.RuleParameters;
            return mvdTemplateRule;
        }

        // each list is optional- if specified then must be followed; if null, then no filter applies (all included)
        internal static void ExportMvd(
            mvdXML mvd,
            DocProject docProject,
            Dictionary<DocObject, bool> included)
        {
            mvd.Name = String.Empty;

            foreach (DocTemplateDefinition docTemplateDef in docProject.Templates)
            {
                if (included == null || included.ContainsKey(docTemplateDef))
                {
                    ConceptTemplate mvdConceptTemplate = new ConceptTemplate();
                    mvd.Templates.Add(mvdConceptTemplate);
                    ExportMvdTemplate(mvdConceptTemplate, docTemplateDef, included);
                }
            }

            foreach (DocModelView docModelView in docProject.ModelViews)
            {
                if (included == null || included.ContainsKey(docModelView))
                {
                    ModelView mvdModelView = new ModelView();
                    mvd.Views.Add(mvdModelView);
                    ExportMvdObject(mvdModelView, docModelView);
                    mvdModelView.ApplicableSchema = "IFC4";
                    mvdModelView.BaseView = docModelView.BaseView;

                    foreach (DocExchangeDefinition docExchangeDef in docModelView.Exchanges)
                    {
                        ExchangeRequirement mvdExchangeDef = new ExchangeRequirement();
                        mvdModelView.ExchangeRequirements.Add(mvdExchangeDef);
                        ExportMvdObject(mvdExchangeDef, docExchangeDef);
                        switch (docExchangeDef.Applicability)
                        {
                            case DocExchangeApplicabilityEnum.Export:
                                mvdExchangeDef.Applicability = ApplicabilityEnum.Export;
                                break;

                            case DocExchangeApplicabilityEnum.Import:
                                mvdExchangeDef.Applicability = ApplicabilityEnum.Import;
                                break;
                        }
                    }

                    // export template usages for model view
                    foreach (DocConceptRoot docRoot in docModelView.ConceptRoots)
                    {
                        if (docRoot.ApplicableEntity != null)
                        {
                            // check if entity contains any concept roots
                            ConceptRoot mvdConceptRoot = new ConceptRoot();
                            mvdModelView.Roots.Add(mvdConceptRoot);

                            ExportMvdObject(mvdConceptRoot, docRoot);
                            mvdConceptRoot.ApplicableRootEntity = docRoot.ApplicableEntity.Name;
                            if (docRoot.ApplicableTemplate != null)
                            {
                                mvdConceptRoot.Applicability = new ApplicabilityRules();
                                mvdConceptRoot.Applicability.Template = new TemplateRef();
                                mvdConceptRoot.Applicability.Template.Ref = docRoot.ApplicableTemplate.Uuid;
                                mvdConceptRoot.Applicability.TemplateRules = new TemplateRules();

                                mvdConceptRoot.Applicability.TemplateRules.Operator = (TemplateOperator)Enum.Parse(typeof(TemplateOperator), docRoot.ApplicableOperator.ToString());
                                foreach (DocTemplateItem docItem in docRoot.ApplicableItems)
                                {
                                    TemplateRule rule = ExportMvdItem(docItem);
                                    mvdConceptRoot.Applicability.TemplateRules.TemplateRule.Add(rule);
                                }
                            }

                            foreach (DocTemplateUsage docTemplateUsage in docRoot.Concepts)
                            {
                                Concept mvdConceptLeaf = new Concept();
                                mvdConceptRoot.Concepts.Add(mvdConceptLeaf);
                                ExportMvdConcept(mvdConceptLeaf, docTemplateUsage);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Helper function to populate attributes
        /// </summary>
        /// <param name="mvd"></param>
        /// <param name="doc"></param>
        private static void ExportMvdObject(Element mvd, DocObject doc)
        {
            mvd.Name = doc.Name;
            mvd.Uuid = doc.Uuid;
            mvd.Version = doc.Version;
            mvd.Owner = doc.Owner;
            mvd.Status = doc.Status;
            mvd.Copyright = doc.Copyright;
            mvd.Code = doc.Code;
            mvd.Author = doc.Author;

            if(mvd.Name == null)
            {
                mvd.Name = String.Empty;
            }

            if (doc.Documentation != null)
            {
                Definition mvdDef = new Definition();
                mvdDef.Body = new Body();
                mvdDef.Body.Content = doc.Documentation;

                mvd.Definitions = new List<Definition>();
                mvd.Definitions.Add(mvdDef);

                if (doc.Localization != null && doc.Localization.Count > 0)
                {
                    mvdDef.Links = new List<Link>();
                    foreach (DocLocalization docLocal in doc.Localization)
                    {
                        Link mvdLink = new Link();
                        mvdDef.Links.Add(mvdLink);
                        mvdLink.Title = docLocal.Name;
                        mvdLink.Content = docLocal.Documentation;
                        mvdLink.Lang = docLocal.Locale;
                        mvdLink.Href = docLocal.URL;
                        mvdLink.Category = (CategoryEnum)(int)docLocal.Category;
                    }
                }
            }
        }

        private static void ExportMvdTemplate(ConceptTemplate mvdTemplate, DocTemplateDefinition docTemplateDef, Dictionary<DocObject, bool> included)
        {
            ExportMvdObject(mvdTemplate, docTemplateDef);
            mvdTemplate.ApplicableEntity = docTemplateDef.Type;

            if (docTemplateDef.Rules != null && docTemplateDef.Rules.Count > 0)
            {
                mvdTemplate.Rules = new List<AttributeRule>();

                foreach (DocModelRule docRule in docTemplateDef.Rules)
                {
                    AttributeRule mvdAttr = new AttributeRule();
                    mvdTemplate.Rules.Add(mvdAttr);
                    ExportMvdRule(mvdAttr, docRule);
                }
            }

            // recurse through sub-templates
            if (docTemplateDef.Templates != null && docTemplateDef.Templates.Count > 0)
            {
                mvdTemplate.SubTemplates = new List<ConceptTemplate>();

                foreach (DocTemplateDefinition docSub in docTemplateDef.Templates)
                {
                    if (included == null || included.ContainsKey(docSub))
                    {
                        ConceptTemplate mvdSub = new ConceptTemplate();
                        mvdTemplate.SubTemplates.Add(mvdSub);
                        ExportMvdTemplate(mvdSub, docSub, included);
                    }
                }
            }
        }

#if false
        private static CardinalityType ExportCardinalityType(DocModelRule rule)
        {
            if (rule.CardinalityMin == 0 && rule.CardinalityMax == 0)
            {
                return CardinalityType._asSchema;
            }
            else if (rule.CardinalityMin == -1 && rule.CardinalityMax == -1)
            {
                return CardinalityType.Zero;
            }
            else if (rule.CardinalityMin == 0 && rule.CardinalityMax == 1)
            {
                return CardinalityType.ZeroToOne;
            }
            else if (rule.CardinalityMin == 1 && rule.CardinalityMax == 1)
            {
                return CardinalityType.One;
            }
            else if (rule.CardinalityMin == 1 && rule.CardinalityMax == -1)
            {
                return CardinalityType.OneToMany;
            }

            return CardinalityType._asSchema;
        }
#endif

        private static void ExportMvdRule(AttributeRule mvdRule, DocModelRule docRule)
        {
            mvdRule.RuleID = docRule.Identification;
            mvdRule.Description = docRule.Description;
            mvdRule.AttributeName = docRule.Name;
            //mvdRule.Cardinality = ExportCardinalityType(docRule);

            foreach (DocModelRule docRuleEntity in docRule.Rules)
            {
                if (docRuleEntity is DocModelRuleEntity)
                {
                    if(mvdRule.EntityRules == null)
                    {
                        mvdRule.EntityRules = new List<EntityRule>();
                    }

                    EntityRule mvdRuleEntity = new EntityRule();
                    mvdRule.EntityRules.Add(mvdRuleEntity);
                    mvdRuleEntity.RuleID = docRuleEntity.Identification;
                    mvdRuleEntity.Description = docRuleEntity.Description;
                    mvdRuleEntity.EntityName = docRuleEntity.Name;
                    //mvdRuleEntity.Cardinality = ExportCardinalityType(docRuleEntity);

                    foreach (DocModelRule docRuleAttribute in docRuleEntity.Rules)
                    {
                        if (docRuleAttribute is DocModelRuleAttribute)
                        {
                            if(mvdRuleEntity.AttributeRules == null)
                            {
                                mvdRuleEntity.AttributeRules = new List<AttributeRule>();
                            }

                            AttributeRule mvdRuleAttribute = new AttributeRule();
                            mvdRuleEntity.AttributeRules.Add(mvdRuleAttribute);
                            ExportMvdRule(mvdRuleAttribute, docRuleAttribute);
                        }
                        else if (docRuleAttribute is DocModelRuleConstraint)
                        {
                            if(mvdRuleEntity.Constraints == null)
                            {
                                mvdRuleEntity.Constraints = new List<Constraint>();
                            }

                            Constraint mvdConstraint = new Constraint();
                            mvdRuleEntity.Constraints.Add(mvdConstraint);
                            mvdConstraint.Expression = docRuleAttribute.Description;
                        }
                    }

                    DocModelRuleEntity dme = (DocModelRuleEntity)docRuleEntity;
                    if(dme.References.Count > 0)
                    {
                        mvdRuleEntity.References = new List<TemplateRef>();
                        foreach (DocTemplateDefinition dtd in dme.References)
                        {
                            TemplateRef tr = new TemplateRef();
                            tr.Ref = dtd.Uuid;
                            mvdRuleEntity.References.Add(tr);
                        }
                    }
                }
                else if (docRuleEntity is DocModelRuleConstraint)
                {
                    if(mvdRule.Constraints == null)
                    {
                        mvdRule.Constraints = new List<Constraint>();
                    }

                    Constraint mvdConstraint = new Constraint();
                    mvdRule.Constraints.Add(mvdConstraint);
                    mvdConstraint.Expression = docRuleEntity.Description;
                }
            }
        }

        internal static void ExportCnfAttribute(IfcDoc.Schema.CNF.entity ent, DocAttribute docAttr, DocXsdFormatEnum xsdformat, bool? tagless)
        {
            Schema.CNF.exp_attribute exp = Schema.CNF.exp_attribute.unspecified;
            bool keep = true;
            switch (xsdformat)
            {
                case DocXsdFormatEnum.Content:
                    exp = Schema.CNF.exp_attribute.attribute_content;
                    break;

                case DocXsdFormatEnum.Attribute:
                    exp = Schema.CNF.exp_attribute.attribute_tag;
                    break;

                case DocXsdFormatEnum.Element:
                    exp = Schema.CNF.exp_attribute.double_tag;
                    break;

                case DocXsdFormatEnum.Hidden:
                    keep = false;
                    break;

                default:
                    break;
            }

            if (!String.IsNullOrEmpty(docAttr.Inverse))
            {
                IfcDoc.Schema.CNF.inverse inv = new Schema.CNF.inverse();
                inv.select = docAttr.Name;
                inv.exp_attribute = exp;
                ent.inverse.Add(inv);
            }
            else
            {
                IfcDoc.Schema.CNF.attribute att = new Schema.CNF.attribute();
                att.select = docAttr.Name;
                att.exp_attribute = exp;
                att.keep = keep;

                if (tagless != null)
                {
                    if (tagless == true)
                    {
                        att.tagless = Schema.CNF.boolean_or_unspecified.boolean_true;
                    }
                    else
                    {
                        att.tagless = Schema.CNF.boolean_or_unspecified.boolean_false;
                    }
                }
                ent.attribute.Add(att);
            }

        }

        internal static void ExportCnf(IfcDoc.Schema.CNF.configuration cnf, DocProject docProject, DocModelView[] docViews, Dictionary<DocObject, bool> included)
        {
            // configure general settings

/*
  <cnf:option inheritance="true" exp-type="unspecified" concrete-attribute="attribute-content" entity-attribute="double-tag" tagless="unspecified" naming-convention="preserve-case" generate-keys="false"/>
  <cnf:schema targetNamespace="http://www.buildingsmart-tech.org/ifcXML/IFC4/final" embed-schema-items="true" elementFormDefault="qualified" attributeFormDefault="unqualified">
    <cnf:namespace prefix="ifc" alias="http://www.buildingsmart-tech.org/ifcXML/IFC4/final"/>
  </cnf:schema>
  <cnf:uosElement name="ifcXML"/>
  <cnf:type select="NUMBER" map="xs:double"/>
  <cnf:type select="BINARY" map="xs:hexBinary"/>  
  <cnf:type select="IfcStrippedOptional" keep="false"/>
*/
            cnf.id = "IFC4";

            IfcDoc.Schema.CNF.option opt = new Schema.CNF.option();
            opt.inheritance = true;
            opt.exp_type = Schema.CNF.exp_type.unspecified;
            opt.concrete_attribute = Schema.CNF.exp_attribute_global.attribute_content;
            opt.entity_attribute = Schema.CNF.exp_attribute_global.double_tag;
            opt.tagless = Schema.CNF.boolean_or_unspecified.unspecified; 
            opt.naming_convention = Schema.CNF.naming_convention.preserve_case;
            opt.generate_keys = false;
            cnf.option.Add(opt);

            IfcDoc.Schema.CNF.schema sch = new IfcDoc.Schema.CNF.schema();
            sch.targetNamespace = "http://www.buildingsmart-tech.org/ifcXML/IFC4/final"; //... make parameter...
            sch.embed_schema_items = true;
            sch.elementFormDefault = Schema.CNF.qual.qualified;
            sch.attributeFormDefault = Schema.CNF.qual.unqualified;

            IfcDoc.Schema.CNF._namespace ns = new Schema.CNF._namespace();
            ns.prefix = "ifc";
            ns.alias = "http://www.buildingsmart-tech.org/ifcXML/IFC4/final";
            sch._namespace = ns;

            cnf.schema.Add(sch);

            IfcDoc.Schema.CNF.uosElement uos = new Schema.CNF.uosElement();
            uos.name = "ifcXML";
            cnf.uosElement.Add(uos);

            IfcDoc.Schema.CNF.type typeNumber = new Schema.CNF.type();
            typeNumber.select = "NUMBER";
            typeNumber.map = "xs:double";
            cnf.type.Add(typeNumber);

            IfcDoc.Schema.CNF.type typeBinary = new Schema.CNF.type();
            typeBinary.select = "BINARY";
            typeBinary.map = "xs:hexBinary";
            cnf.type.Add(typeBinary);

            IfcDoc.Schema.CNF.type typeStripped = new Schema.CNF.type();
            typeStripped.select = "IfcStrippedOptional";
            typeStripped.keep = false;
            cnf.type.Add(typeStripped);

            SortedDictionary<string, IfcDoc.Schema.CNF.entity> mapEntity = new SortedDictionary<string, Schema.CNF.entity>();

            // export default configuration -- also export for Common Use Definitions (base view defined as itself to include everything)
            //if (docViews == null || docViews.Length == 0 || (docViews.Length == 1 && docViews[0].BaseView == docViews[0].Uuid.ToString()))
            {
                foreach (DocSection docSection in docProject.Sections)
                {
                    foreach (DocSchema docSchema in docSection.Schemas)
                    {
                        foreach(DocEntity docEntity in docSchema.Entities)
                        {
                            bool include = true; //... check if included in graph?
                            if (included != null && !included.ContainsKey(docEntity))
                            {
                                include = false;
                            }

                            if (include)
                            {
                                foreach (DocAttribute docAttr in docEntity.Attributes)
                                {
                                    if (docAttr.XsdFormat != DocXsdFormatEnum.Default || docAttr.XsdTagless == true)
                                    {
                                        IfcDoc.Schema.CNF.entity ent = null;
                                        if (!mapEntity.TryGetValue(docEntity.Name, out ent))
                                        {
                                            ent = new Schema.CNF.entity();
                                            ent.select = docEntity.Name;
                                            mapEntity.Add(docEntity.Name, ent);
                                        }

                                        ExportCnfAttribute(ent, docAttr, docAttr.XsdFormat, docAttr.XsdTagless);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // export view-specific configuration
            foreach (DocModelView docView in docViews)
            {
                foreach (DocXsdFormat format in docView.XsdFormats)
                {
                    DocEntity docEntity = docProject.GetDefinition(format.Entity) as DocEntity;
                    if (docEntity != null)
                    {
                        DocAttribute docAttr = null;
                        foreach (DocAttribute docEachAttr in docEntity.Attributes)
                        {
                            if (docEachAttr.Name != null && docEachAttr.Name.Equals(format.Attribute))
                            {
                                docAttr = docEachAttr;
                                break;
                            }
                        }

                        if (docAttr != null)
                        {
                            IfcDoc.Schema.CNF.entity ent = null;
                            if (!mapEntity.TryGetValue(docEntity.Name, out ent))
                            {
                                ent = new Schema.CNF.entity();
                                mapEntity.Add(docEntity.Name, ent);
                            }

                            ExportCnfAttribute(ent, docAttr, format.XsdFormat, format.XsdTagless);
                        }
                    }
                }
            }

            // add at end, such that sorted
            foreach(IfcDoc.Schema.CNF.entity ent in mapEntity.Values)
            {
                cnf.entity.Add(ent);
            }
        }

        internal static void ImportCnfAttribute(IfcDoc.Schema.CNF.exp_attribute exp_attribute, bool keep, Schema.CNF.boolean_or_unspecified tagless, DocEntity docEntity, DocAttribute docAttribute, DocModelView docView)
        {
            DocXsdFormatEnum xsdformat = DocXsdFormatEnum.Default;
            if (exp_attribute == Schema.CNF.exp_attribute.attribute_content)
            {
                xsdformat = DocXsdFormatEnum.Content;
            }
            else if (exp_attribute == Schema.CNF.exp_attribute.attribute_tag)
            {
                xsdformat = DocXsdFormatEnum.Attribute;
            }
            else if (exp_attribute == Schema.CNF.exp_attribute.double_tag)
            {
                xsdformat = DocXsdFormatEnum.Element;
            }
            else if (!keep)
            {
                xsdformat = DocXsdFormatEnum.Hidden;
            }
            else
            {
                xsdformat = DocXsdFormatEnum.Element;
            }

            bool? booltagless = null;
            switch (tagless)
            {
                case Schema.CNF.boolean_or_unspecified.boolean_true:
                    booltagless = true;
                    break;

                case Schema.CNF.boolean_or_unspecified.boolean_false:
                    booltagless = false;
                    break;
            }

            if (docView != null)
            {
                // configure specific model view
                DocXsdFormat docFormat = new DocXsdFormat();
                docFormat.Entity = docEntity.Name;
                docFormat.Attribute = docAttribute.Name;
                docFormat.XsdFormat = xsdformat;
                docFormat.XsdTagless = booltagless;
                docView.XsdFormats.Add(docFormat);
            }
            else
            {
                // configure default
                docAttribute.XsdFormat = xsdformat;
                docAttribute.XsdTagless = booltagless;
            }

        }

        internal static void ImportCnf(IfcDoc.Schema.CNF.configuration cnf, DocProject docProject, DocModelView docView)
        {
            Dictionary<string, DocEntity> mapEntity = new Dictionary<string, DocEntity>();
            foreach (DocSection docSection in docProject.Sections)
            {
                foreach (DocSchema docSchema in docSection.Schemas)
                {
                    foreach (DocEntity docEntity in docSchema.Entities)
                    {
                        mapEntity.Add(docEntity.Name, docEntity);
                    }
                }
            }

            foreach (IfcDoc.Schema.CNF.entity ent in cnf.entity)
            {
                DocEntity docEntity = null;
                if (mapEntity.TryGetValue(ent.select, out docEntity))
                {
                    if (ent.attribute != null)
                    {
                        foreach (IfcDoc.Schema.CNF.attribute atr in ent.attribute)
                        {
                            // find attribute on entity
                            foreach (DocAttribute docAttribute in docEntity.Attributes)
                            {
                                if (atr.select != null && atr.select.Equals(docAttribute.Name))
                                {
                                    ImportCnfAttribute(atr.exp_attribute, atr.keep, atr.tagless, docEntity, docAttribute, docView);
                                }
                            }
                        }
                    }

                    if (ent.inverse != null)
                    {
                        foreach (IfcDoc.Schema.CNF.inverse inv in ent.inverse)
                        {
                            // find attribute on entity
                            foreach (DocAttribute docAttribute in docEntity.Attributes)
                            {
                                if (inv.select != null && inv.select.Equals(docAttribute.Name))
                                {
                                    ImportCnfAttribute(inv.exp_attribute, true, Schema.CNF.boolean_or_unspecified.unspecified, docEntity, docAttribute, docView);
                                }
                            }
                        }
                    }

                }
            }
        }

        public static void ExportSch(IfcDoc.Schema.SCH.schema schema, DocProject docProject, Dictionary<DocObject, bool> included)
        {
            Dictionary<DocExchangeDefinition, phase> mapPhase = new Dictionary<DocExchangeDefinition, phase>();

            foreach (DocModelView docModel in docProject.ModelViews)
            {
                if (included == null || included.ContainsKey(docModel))
                {
                    foreach (DocExchangeDefinition docExchange in docModel.Exchanges)
                    {
                        phase ph = new phase();
                        schema.Phases.Add(ph);
                        ph.id = docExchange.Name.ToLower().Replace(" ", "-");

                        mapPhase.Add(docExchange, ph);
                    }

                    foreach (DocConceptRoot docRoot in docModel.ConceptRoots)
                    {
                        foreach (DocTemplateUsage docConcept in docRoot.Concepts)
                        {
                            pattern pat = new pattern();
                            schema.Patterns.Add(pat);

                            pat.id = docRoot.ApplicableEntity.Name.ToLower() + "-" + docConcept.Definition.Name.ToLower().Replace(" ", "-");// docConcept.Uuid.ToString();
                            pat.name = docConcept.Definition.Name;
                            pat.p = docConcept.Documentation;

                            foreach (DocExchangeItem docExchangeUsage in docConcept.Exchanges)
                            {
                                if (docExchangeUsage.Applicability == DocExchangeApplicabilityEnum.Export && docExchangeUsage.Requirement == DocExchangeRequirementEnum.Mandatory)
                                {
                                    phase ph = mapPhase[docExchangeUsage.Exchange];
                                    active a = new active();
                                    a.pattern = pat.id;
                                    ph.Actives.Add(a);
                                }
                            }

                            // recurse through template rules

                            List<DocModelRule> listParamRules = new List<DocModelRule>();
                            if (docConcept.Definition.Rules != null)
                            {
                                foreach (DocModelRule docRule in docConcept.Definition.Rules)
                                {
                                    docRule.BuildParameterList(listParamRules);
                                }
                            }


                            List<DocModelRule[]> listPaths = new List<DocModelRule[]>();
                            foreach (DocModelRule docRule in listParamRules)
                            {
                                DocModelRule[] rulepath = docConcept.Definition.BuildRulePath(docRule);
                                listPaths.Add(rulepath);
                            }


                            if (docConcept.Items.Count > 0)
                            {
                                foreach (DocTemplateItem docItem in docConcept.Items)
                                {
                                    rule r = new rule();
                                    pat.Rules.Add(r);

                                    r.context = "//" + docRoot.ApplicableEntity.Name;

                                    //TODO: detect constraining parameter and generate XPath...
                                    for(int iRule = 0; iRule < listParamRules.Count; iRule++)// (DocModelRule docRule in listParamRules)
                                    {
                                        DocModelRule docRule = listParamRules[iRule];
                                        if (docRule.IsCondition())  
                                        {
                                            DocModelRule[] docPath = listPaths[iRule];

                                            StringBuilder sbContext = new StringBuilder();
                                            sbContext.Append("[@");
                                            for (int iPart = 0; iPart < docPath.Length; iPart++)
                                            {
                                                sbContext.Append(docPath[iPart].Name);
                                                sbContext.Append("/");
                                            }

                                            sbContext.Append(" = ");
                                            string cond = docItem.GetParameterValue(docRule.Identification);
                                            sbContext.Append(cond);

                                            sbContext.Append("]");

                                            r.context += sbContext.ToString();
                                        }
                                    }

                                    for (int iRule = 0; iRule < listParamRules.Count; iRule++)// (DocModelRule docRule in listParamRules)
                                    {
                                        DocModelRule docRule = listParamRules[iRule];

                                        if (!docRule.IsCondition())
                                        {
                                            string value = docItem.GetParameterValue(docRule.Identification);
                                            if (value != null)
                                            {
                                                DocModelRule[] docPath = listPaths[iRule];

                                                StringBuilder sbContext = new StringBuilder();
                                                for (int iPart = 0; iPart < docPath.Length; iPart++)
                                                {
                                                    sbContext.Append(docPath[iPart].Name);
                                                    sbContext.Append("/");
                                                }

                                                sbContext.Append(" = '");
                                                sbContext.Append(value);
                                                sbContext.Append("'");

                                                assert a = new assert();
                                                a.test = sbContext.ToString(); 
                                                r.Asserts.Add(a);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // recurse through each rule
                                List<DocModelRule> pathRule = new List<DocModelRule>();
                                foreach (DocModelRule docModelRule in docConcept.Definition.Rules)
                                {
                                    pathRule.Add(docModelRule);

                                    rule r = new rule();
                                    r.context = "//" + docRoot.ApplicableEntity;
                                    pat.Rules.Add(r);

                                    ExportSchRule(r, pathRule);

                                    pathRule.Remove(docModelRule);
                                }
                            }
                        }

                    }
                }


            }
        }

        private static void ExportSchRule(rule r, List<DocModelRule> pathRule)
        {
            assert a = new assert();
            r.Asserts.Add(a);

            StringBuilder sb = new StringBuilder();
            foreach (DocModelRule docRule in pathRule)
            {
                sb.Append(docRule.Name);
                sb.Append("/");
            }
            a.test = sb.ToString();

            // recurse
            DocModelRule docParent = pathRule[pathRule.Count-1];
            if (docParent.Rules != null)
            {
                foreach (DocModelRule docSub in docParent.Rules)
                {
                    pathRule.Add(docSub);

                    ExportSchRule(r, pathRule);

                    pathRule.Remove(docSub);
                }
            }
        }

        internal static string ImportXsdType(string xsdtype)
        {
            if (xsdtype == null)
                return null;

            switch (xsdtype)
            {
                case "xs:string":
                case "xs:anyURI":
                    return "STRING";

                case "xs:decimal":
                case "xs:float":
                case "xs:double":
                    return "REAL";

                case "xs:integer":
                case "xs:byte": // signed 8-bit
                case "xs:short": // signed 16-bit
                case "xs:int": // signed 32-bit
                case "xs:long": // signed 64-bit
                    return "INTEGER";

                case "xs:boolean":
                    return "BOOLEAN";

                case "xs:base64Binary":
                case "xs:hexBinary":
                    return "BINARY";

                default:
                    return xsdtype;
            }
        }

        internal static string ImportXsdAnnotation(IfcDoc.Schema.XSD.annotation annotation)
        {
            if (annotation == null)
                return null;
            
            StringBuilder sb = new StringBuilder();
            foreach (string s in annotation.documentation)
            {
                sb.AppendLine("<p>");
                sb.AppendLine(s);
                sb.AppendLine("</p>");
            }
            return sb.ToString();            
        }

        internal static void ImportXsdElement(IfcDoc.Schema.XSD.element sub, DocEntity docEntity, bool list)
        {
            DocAttribute docAttr = new DocAttribute();
            docEntity.Attributes.Add(docAttr);
            if (!String.IsNullOrEmpty(sub.name))
            {
                docAttr.Name = sub.name;
            }
            else
            {
                docAttr.Name = sub.reftype;
            }

            if (!String.IsNullOrEmpty(sub.type))
            {
                docAttr.DefinedType = sub.type;
            }
            else
            {
                docAttr.DefinedType = sub.reftype;
            }
            // list or set??...

            if (list || sub.minOccurs != null)
            {
                if (list || sub.maxOccurs != null)
                {
                    // list
                    if (list || sub.maxOccurs == "unbounded")
                    {
                        docAttr.AggregationType = 1; // list
                        if (!String.IsNullOrEmpty(sub.minOccurs))
                        {
                            docAttr.AggregationLower = sub.minOccurs;
                        }
                        else
                        {
                            docAttr.AggregationLower = "0";
                        }
                        docAttr.AggregationUpper = "?";
                    }
                }
                else if (sub.minOccurs == "0")
                {
                    docAttr.IsOptional = true;
                }
            }

            docAttr.Documentation = ImportXsdAnnotation(sub.annotation);
        }

        internal static void ImportXsdSimple(IfcDoc.Schema.XSD.simpleType simple, DocSchema docSchema, string name)
        {
            string thename = simple.name;
            if (simple.name == null)
            {
                thename = name;
            }

            if (simple.restriction != null && simple.restriction.enumeration.Count > 0)
            {
                DocEnumeration docEnum = new DocEnumeration();
                docSchema.Types.Add(docEnum);
                docEnum.Name = thename;
                docEnum.Documentation = ImportXsdAnnotation(simple.annotation);
                foreach (IfcDoc.Schema.XSD.enumeration en in simple.restriction.enumeration)
                {
                    DocConstant docConst = new DocConstant();
                    docConst.Name = en.value;
                    docConst.Documentation = ImportXsdAnnotation(en.annotation);

                    docEnum.Constants.Add(docConst);
                }
            }
            else
            {
                DocDefined docDef = new DocDefined();
                docDef.Name = thename;
                docDef.Documentation = ImportXsdAnnotation(simple.annotation);
                if (simple.restriction != null)
                {
                    docDef.DefinedType = ImportXsdType(simple.restriction.basetype);
                }
                docSchema.Types.Add(docDef);
            }
        }

        internal static void ImportXsdAttribute(IfcDoc.Schema.XSD.attribute att, DocSchema docSchema, DocEntity docEntity)
        {
            DocAttribute docAttr = new DocAttribute();
            docEntity.Attributes.Add(docAttr);
            docAttr.Name = att.name;
            docAttr.IsOptional = (att.use == Schema.XSD.use.optional);

            if (att.simpleType != null)
            {
                string refname = docEntity.Name + "_" + att.name;
                docAttr.DefinedType = refname;
                ImportXsdSimple(att.simpleType, docSchema, refname);
            }
            else
            {
                docAttr.DefinedType = ImportXsdType(att.type);
            }
        }

        internal static void ImportXsdComplex(IfcDoc.Schema.XSD.complexType complex, DocSchema docSchema, DocEntity docEntity)
        {
            if (complex == null)
                return;

            foreach (IfcDoc.Schema.XSD.attribute att in complex.attribute)
            {
                ImportXsdAttribute(att, docSchema, docEntity);
            }

            if (complex.choice != null)
            {
                foreach (IfcDoc.Schema.XSD.element sub in complex.choice.element)
                {
                    ImportXsdElement(sub, docEntity, true);
                }
            }

            if (complex.sequence != null)
            {
                foreach (IfcDoc.Schema.XSD.element sub in complex.sequence.element)
                {
                    ImportXsdElement(sub, docEntity, true);
                }
            }

            if (complex.all != null)
            {
                foreach (IfcDoc.Schema.XSD.element sub in complex.all.element)
                {
                    ImportXsdElement(sub, docEntity, true);
                }
            }

            if (complex.complexContent != null)
            {
                if(complex.complexContent.extension != null)
                {
                    docEntity.BaseDefinition = complex.complexContent.extension.basetype;

                    foreach (IfcDoc.Schema.XSD.attribute att in complex.complexContent.extension.attribute)
                    {
                        ImportXsdAttribute(att, docSchema, docEntity);
                    }

                    if(complex.complexContent.extension.choice != null)
                    {
                        foreach (IfcDoc.Schema.XSD.element sub in complex.complexContent.extension.choice.element)
                        {
                            ImportXsdElement(sub, docEntity, true);
                        }
                    }
                }
            }
        }

        internal static DocSchema ImportXsd(IfcDoc.Schema.XSD.schema schema, DocProject docProject)
        {
            // use resource-level section
            DocSection docSection = docProject.Sections[6]; // source schemas

            DocSchema docSchema = new DocSchema();
            docSchema.Name = schema.id;//??
            docSchema.Code = schema.id;
            docSchema.Version = schema.version;
            docSection.Schemas.Add(docSchema);

            foreach(IfcDoc.Schema.XSD.simpleType simple in schema.simpleType)
            {
                ImportXsdSimple(simple, docSchema, null);
            }

            foreach (IfcDoc.Schema.XSD.complexType complex in schema.complexType)
            {
                DocEntity docEntity = new DocEntity();
                docSchema.Entities.Add(docEntity);
                docEntity.Name = complex.name;
                docEntity.Documentation = ImportXsdAnnotation(complex.annotation);

                ImportXsdComplex(complex, docSchema, docEntity);
            }

            foreach (IfcDoc.Schema.XSD.element element in schema.element)
            {
                DocEntity docEntity = new DocEntity();
                docSchema.Entities.Add(docEntity);
                docEntity.Name = element.name;
                docEntity.Documentation = ImportXsdAnnotation(element.annotation);

                ImportXsdComplex(element.complexType, docSchema, docEntity);
            }

            return docSchema;
        }

    }

}
