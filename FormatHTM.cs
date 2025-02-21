﻿// Name:        FormatHtm.cs
// Description: HTML document generation
// Author:      Tim Chipman
// Origination: Work performed for BuildingSmart by Constructivity.com LLC.
// Copyright:   (c) 2010 BuildingSmart International Ltd.
// License:     http://www.buildingsmart-tech.org/legal

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using IfcDoc.Schema.DOC;

namespace IfcDoc.Format.HTM
{
    public class FormatHTM : 
        IDisposable,
        IComparer<string>
    {
        Stream m_stream;
        StreamWriter m_writer;
        Dictionary<string, DocObject> m_mapEntity;
        Dictionary<string, string> m_mapSchema;
        bool m_anchors; // if true, hyperlinks are anchors within same page; if false, hyperlinks go to documentation
        Dictionary<DocObject, bool> m_included;

        const string BEGIN_KEYWORD = "<span class=\"keyword\">";
        const string END_KEYWORD = "</span>";

        public FormatHTM(string path, Dictionary<string, DocObject> mapEntity, Dictionary<string, string> mapSchema, Dictionary<DocObject, bool> included)
        {
            string dirpath = System.IO.Path.GetDirectoryName(path);
            if (!Directory.Exists(dirpath))
            {
                Directory.CreateDirectory(dirpath);
            }

            this.m_stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
            this.m_writer = new StreamWriter(this.m_stream, Encoding.UTF8);

            this.m_mapEntity = mapEntity;
            this.m_mapSchema = mapSchema;
            this.m_included = included;
        }

        public bool UseAnchors
        {
            get
            {
                return this.m_anchors;
            }
            set
            {
                this.m_anchors = value;
            }
        }

        public Dictionary<DocObject, bool> Included
        {
            get
            {
                return this.m_included;
            }
        }

        #region IDisposable Members

        public void Dispose()
        {
            if (this.m_writer != null)
            {
                this.m_writer.Flush();
                this.m_writer.Close();
            }

            if (this.m_stream != null)
            {
                this.m_stream.Close();
                this.m_stream = null;
            }
        }

        #endregion        

        public void WriteHeader(string title, int level)
        {
            WriteHeader(title, level, 0, 0, 0, 0);
        }

        /// <summary>
        /// Writes opening HTML
        /// </summary>
        /// <param name="title">Caption</param>
        /// <param name="level">Number of levels deep (for referencing style sheet)</param>
        public void WriteHeader(string title, int level, int section, int schema, int category, int definition)
        {
            string style = "";

            for (int i = 0; i < level; i++)
            {
                style += "../";
            }

            this.m_writer.WriteLine("<!DOCTYPE HTML>\r\n");
            this.m_writer.WriteLine("<html>");
            this.m_writer.WriteLine("  <head>");
            this.m_writer.WriteLine("    <meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\">\r\n");
            this.m_writer.WriteLine("    <link rel=\"stylesheet\" type=\"text/css\" href=\"" + style + "ifc-styles.css\">\r\n");
            this.m_writer.WriteLine("    <link rel=\"stylesheet\" type=\"text/css\" href=\"" + style + "details-shim.css\">");
	        this.m_writer.WriteLine("    <script type=\"text/javascript\" src=\"" + style + "details-shim.js\"></script>");
            this.m_writer.WriteLine("    <title>" + title + "</title>\r\n");

            this.m_writer.WriteLine("    <style type=\"text/css\">");
            this.m_writer.WriteLine("      body { counter-reset: index1 " + section + " index2 " + schema + " index3 " + category + " index4 " + (definition - 1) + " index5 0; }");
            this.m_writer.WriteLine("    </style>");

            this.m_writer.WriteLine("  </head>\r\n");
            this.m_writer.WriteLine("  <body>\r\n");

            if (!String.IsNullOrEmpty(Properties.Settings.Default.Header))
            {
                this.m_writer.WriteLine("<p>" + Properties.Settings.Default.Header + "</p>");
            }
        }


        /// <summary>
        /// Old function for compatibility
        /// </summary>
        /// <param name="title"></param>
        /// <param name="section"></param>
        /// <param name="schema"></param>
        /// <param name="definition"></param>
        /// <param name="header"></param>
        public void WriteHeader(string title, int section, int schema, int category, int definition, string header)
        {
            int level = 0;

            if (schema == 0)
            {
                // section page
                level = 1;
            }
            else if (section < 0)
            {
                // annex
                if (section == -2 || section == -3)
                {
                    level = 2;
                }
                else
                {
                    level = 3;
                }
            }
            else if (section == 1)
            {
                // views
                level = 3;
            }
            else if (section == 4)
            {
                level = 2;
            }
            else if (definition > 0)
            {
                level = 3;
            }
            else if (schema > 0)
            {
                level = 2;
            }
            else if (section > 0)
            {
            }
            else if (title != "Table of Contents" && title != "Index")
            {
                level = 2;
            }

            WriteHeader(title, level, section, schema, category, definition);
        }

        /// <summary>
        /// Writes closing tags
        /// </summary>
        public void WriteFooter(string footer)
        {
            if (footer != null)
            {
                this.m_writer.WriteLine("<p>" + footer + "</p>");
            }

            this.m_writer.WriteLine("  </body>");
            this.m_writer.WriteLine("</html>");
        }

        public void Write(string content)
        {
            this.m_writer.Write(content);
        }

        public void WriteLine(string content)
        {
            this.m_writer.WriteLine(content);
        }

        public void WriteDefinition(string definition)
        {
            string format = FormatDefinition(definition);
            WriteLine(format);
        }

        /// <summary>
        /// Encodes HTML, preserves tabs and new lines, and generates hyperlinks for IFC references
        /// </summary>
        /// <param name="rawtext"></param>
        public void WriteFormatted(string rawtext)
        {
            string html = System.Web.HttpUtility.HtmlEncode(rawtext.ToString());
            html = html.Replace("\r\n", "<br/>\r\n");
            html = html.Replace("\t", "&nbsp;");

            StringBuilder sb = new StringBuilder();
            int p = 0; // previous
            int i = html.IndexOf(":Ifc", StringComparison.Ordinal);
            while (i != -1)
            {
                int j = html.IndexOf('&', i + 1); // end-quote converted to &quot
                // j should always be valid here

                string def = html.Substring(i + 1, j - i - 1);
                string fmt = FormatDefinition(def);

                sb.Append(html, p, i - p + 1);
                sb.Append(fmt);

                p = j;
                i = html.IndexOf(":Ifc", i + 1, StringComparison.Ordinal);
            }
            sb.Append(html, p, html.Length - p);

            this.Write(sb.ToString());
        }

        /// <summary>
        /// Writes definition, using hyperlink for IFC-defined type (e.g. IfcCartesianPoint) or bold for primitive type (e.g. INTEGER)
        /// </summary>
        /// <param name="definition"></param>
        private string FormatDefinition(string definition)
        {
            DocObject ent = null;
            string schema = null;
            if (definition != null && //definition.StartsWith("Ifc") && 
                this.m_mapEntity.TryGetValue(definition, out ent) && 
                this.m_mapSchema.TryGetValue(definition, out schema))
            {
                if (this.m_included == null || this.m_included.ContainsKey(ent))
                {
                    if (this.m_anchors)
                    {
                        return "<a href=\"#" + definition.ToLower() + "\">" + definition + "</a>";
                    }
                    else
                    {
                        if (this.m_stream is FileStream && ((FileStream)this.m_stream).Name.EndsWith(".xsd.htm"))
                        {
                            string hyperlink = @"../../../schema/" + schema.ToLower() + @"/lexical/" + definition.ToLower() + ".htm";
                            return "<a href=\"" + hyperlink + "\">" + definition + "</a>";
                        }
                        else if(ent is DocPropertySet || ent is DocPropertyEnumeration)
                        {
                            string hyperlink = @"../../" + schema.ToLower() + @"/pset/" + definition.ToLower() + ".htm";
                            return "<a href=\"" + hyperlink + "\">" + definition + "</a>";
                        }
                        else if(ent is DocQuantitySet)
                        {
                            string hyperlink = @"../../" + schema.ToLower() + @"/qset/" + definition.ToLower() + ".htm";
                            return "<a href=\"" + hyperlink + "\">" + definition + "</a>";
                        }
                        else //if (ent is DocDefinition)
                        {
                            string hyperlink = @"../../" + schema.ToLower() + @"/lexical/" + definition.ToLower() + ".htm";
                            return "<a href=\"" + hyperlink + "\">" + definition + "</a>";
                        }
                    }
                }
                else
                {
                    return "<span class=\"self-ref\">IfcStrippedOptional</span>";
                }
            }
            else
            {
                return "<span class=\"self-ref\">" + definition + "</span>";
            }
        }

        public void WriteTOC(int indent, string content)
        {
            for (int i = 0; i < indent; i++)
            {
                // ISO document is incorrect - there is no such "&sp5;" symbol
                this.m_writer.Write("&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;");
            }

            this.m_writer.Write(content);
            this.m_writer.Write("<br />");
            this.m_writer.WriteLine();
        }

        private void WriteExpressHeader(int indent)
        {
#if false
            this.m_writer.WriteLine("<table width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" class=\"express\">");
            this.m_writer.WriteLine(" <tr valign=\"top\">");
            if (indent > 0)
            {
                this.m_writer.WriteLine("  <td width=\"" + indent.ToString() + "%\">");
            }
            this.m_writer.WriteLine("  <td>");
#endif
        }

        private void WriteExpressFooter()
        {
#if false
            this.m_writer.WriteLine("  </td>");
            this.m_writer.WriteLine(" </tr>");
            this.m_writer.WriteLine("</table>");
#endif
        }

        public void WriteExpressLine(int indent, string content)
        {
            for (int i = 0; i < indent; i++ )
            {
                this.m_writer.Write("&nbsp;");
            }

            this.m_writer.Write(content);
            this.m_writer.WriteLine("<br/>");
            /*
            WriteExpressHeader(indent);
            WriteLine(content);
            WriteExpressFooter();
            */
        }

        /// <summary>
        /// Writes EXPRESS attributes for entity definition or as part of inheritance graph
        /// </summary>
        /// <param name="entity">The entity to reflect attributes.</param>
        /// <param name="treeleaf">Optional leaf entity for inheritance diagram; any overridden attributes will be suppressed.</param>
        public void WriteExpressAttributes(DocEntity entity, DocEntity treeleaf)
        {
            bool bInverse = false;
            bool bDerived = false;
            bool bExplicit = false;

            // count attributes first to avoid generating tables unnecessarily (W3C validation)
            if (entity.Attributes != null && entity.Attributes.Count > 0)
            {
                foreach (DocAttribute attr in entity.Attributes)
                {
                    if (attr.Derived != null)
                    {
                        // inverse may also be indicated to hold class
                        bDerived = true;
                    }
                    else if (attr.Inverse != null)
                    {                        
                        bInverse = true;
                    }
                    else
                    {
                        bExplicit = true;
                    }
                }
            }
            

            // explicit attributes, plus detect any inverse or derived
            if (bExplicit)
            {
                this.WriteExpressHeader(2);
                foreach (DocAttribute attr in entity.Attributes)
                {
                    bool bInclude = true;

                    // suppress any attribute that is overridden on leaf class
                    if (treeleaf != null && treeleaf != entity)
                    {
                        foreach (DocAttribute derivedattr in treeleaf.Attributes)
                        {
                            if (derivedattr.Name.Equals(attr.Name))
                            {
                                bInclude = false;
                            }
                        }
                    }

                    if (bInclude)
                    {
                        if (attr.Inverse == null && attr.Derived == null)
                        {
                            this.m_writer.Write("&nbsp;&nbsp;");
                            this.m_writer.Write(attr.Name);
                            this.m_writer.Write(" : ");

                            if ((attr.AttributeFlags & 1) != 0)
                            {
                                this.m_writer.Write(BEGIN_KEYWORD + "OPTIONAL" + END_KEYWORD + " ");
                            }

                            this.WriteExpressAggregation(attr);

                            if (this.m_included == null || this.m_included.ContainsKey(attr))
                            {
                                this.m_writer.Write(FormatDefinition(attr.DefinedType));
                            }
                            else
                            {
                                this.m_writer.Write("<span class=\"self-ref\">IfcStrippedOptional</span>");
                            }
                            this.m_writer.WriteLine(";<br/>");

                        }
                    }
                }
                this.WriteExpressFooter();
            }

            // inverse attributes
            if (bInverse)
            {
                this.WriteExpressLine(1, BEGIN_KEYWORD + "INVERSE" + END_KEYWORD);
                this.WriteExpressHeader(2);
                foreach (DocAttribute attr in entity.Attributes)
                {
                    DocObject docinvtype = null;
                    if (attr.Inverse != null && attr.Derived == null && this.m_mapEntity.TryGetValue(attr.DefinedType, out docinvtype))
                    {
                        if (this.m_included == null || this.m_included.ContainsKey(docinvtype))
                        {
                            this.m_writer.Write("&nbsp;&nbsp;");
                            this.m_writer.Write(attr.Name);
                            this.m_writer.Write(" : ");

                            this.WriteExpressAggregation(attr);

                            this.m_writer.Write(FormatDefinition(attr.DefinedType));
                            this.m_writer.Write(" " + BEGIN_KEYWORD + "FOR" + END_KEYWORD + " ");
                            this.m_writer.Write(attr.Inverse);
                            this.m_writer.WriteLine(";<br/>");
                        }
                    }
                }
                this.WriteExpressFooter();
            }

            // derived attributes
            if (bDerived)
            {
                this.WriteExpressLine(1, BEGIN_KEYWORD + "DERIVE" + END_KEYWORD);
                this.WriteExpressHeader(2);

                foreach (DocAttribute attr in entity.Attributes)
                {
                    if (attr.Derived != null)
                    {
                        // determine the superclass having the attribute                        
                        DocEntity found = null;
                        if (treeleaf == null)
                        {
                            DocEntity super = entity;
                            while (super != null && found == null && super.BaseDefinition != null)
                            {
                                super = this.m_mapEntity[super.BaseDefinition] as DocEntity;
                                if (super != null)
                                {
                                    foreach (DocAttribute docattr in super.Attributes)
                                    {
                                        if (docattr.Name.Equals(attr.Name))
                                        {
                                            // found class
                                            found = super;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        if (found != null)
                        {
                            // overridden attribute
                            this.m_writer.Write("&nbsp;&nbsp;" + BEGIN_KEYWORD + "SELF" + END_KEYWORD + "\\" + found.Name + "." + attr.Name + " : ");
                        }
                        else
                        {
                            // non-overridden
                            this.m_writer.Write("&nbsp;&nbsp;" + attr.Name + " : ");
                        }

                        this.WriteExpressAggregation(attr);

                        this.m_writer.Write(FormatDefinition(attr.DefinedType));

                        this.m_writer.Write(" := ");
                        this.m_writer.Write(attr.Derived);
                        this.m_writer.WriteLine(";<br/>");

                    }
                }
                this.WriteExpressFooter();
            }
        }

        private void WriteAttributeAggregation(DocAttribute attr)
        {
            if (attr.AggregationType == 0)
                return;

            DocAttribute docAggregation = attr;
            while (docAggregation != null)
            {
                if ((docAggregation.AggregationFlag & 2) != 0)
                {
                    // unique
                    this.m_writer.Write("~");
                }

                switch (docAggregation.AggregationType)
                {
                    case 1: // list
                        this.m_writer.Write("L[");
                        break;

                    case 2: // array
                        this.m_writer.Write("A[");
                        break;

                    case 3: // set
                        this.m_writer.Write("S[");
                        break;

                    case 4: // bag
                        this.m_writer.Write("B[");
                        break;
                }

                if (docAggregation.AggregationUpper != null && docAggregation.AggregationUpper != "0")
                {
                    this.m_writer.Write(docAggregation.AggregationLower + ":" + docAggregation.AggregationUpper);
                }
                else if (docAggregation.AggregationLower != null)
                {
                    this.m_writer.Write(docAggregation.AggregationLower + ":?");
                }
                else
                {
                    this.m_writer.Write("0:?");
                }

                switch (docAggregation.AggregationType)
                {
                    case 1: // list
                        this.m_writer.Write("]");
                        break;

                    case 2: // array
                        this.m_writer.Write("]");
                        break;

                    case 3: // set
                        this.m_writer.Write("]");
                        break;

                    case 4: // bag
                        this.m_writer.Write("]");
                        break;
                }

                // drill in
                docAggregation = docAggregation.AggregationAttribute;
            }
        }


        private void WriteExpressAggregation(DocAttribute attr)
        {
            if (attr.AggregationType == 0)
                return;

            DocAttribute docAggregation = attr;
            while (docAggregation != null)
            {
                switch (docAggregation.AggregationType)
                {
                    case 1:
                        this.m_writer.Write(BEGIN_KEYWORD + "LIST" + END_KEYWORD + " ");
                        break;

                    case 2:
                        this.m_writer.Write(BEGIN_KEYWORD + "ARRAY" + END_KEYWORD + " ");
                        break;

                    case 3:
                        this.m_writer.Write(BEGIN_KEYWORD + "SET" + END_KEYWORD + " ");
                        break;

                    case 4:
                        this.m_writer.Write(BEGIN_KEYWORD + "BAG" + END_KEYWORD + " ");
                        break;
                }

                if (docAggregation.AggregationUpper != null && docAggregation.AggregationUpper != "0")
                {
                    this.m_writer.Write("[" + docAggregation.AggregationLower + ":" + docAggregation.AggregationUpper + "] OF ");
                }
                else if (docAggregation.AggregationLower != null)
                {
                    this.m_writer.Write("[" + docAggregation.AggregationLower + ":?] " + BEGIN_KEYWORD + "OF" + END_KEYWORD + " ");
                }
                else
                {
                    this.m_writer.Write(BEGIN_KEYWORD + "OF" + END_KEYWORD + " ");
                }

                if ((docAggregation.AggregationFlag & 2) != 0)
                {
                    // unique
                    this.m_writer.Write(BEGIN_KEYWORD + "UNIQUE" + END_KEYWORD + " ");
                }

                // drill in
                docAggregation = docAggregation.AggregationAttribute;
            }
        }

        public void WriteSummaryHeader(string caption, bool expanded)
        {
            //this.m_writer.Write("<hr />");
            this.m_writer.Write("<details");
            this.m_writer.Write(expanded ? " open=\"open\"" : "");
            this.m_writer.Write("><summary>");
            this.m_writer.Write(caption);
            this.m_writer.Write("</summary>");
            //this.m_writer.WriteLine("<br/>");
        }

        public void WriteSummaryFooter()
        {
            this.m_writer.WriteLine("</details>");
        }

        public void WriteExpressEntityAndDocumentation(DocEntity entity, bool suppresshistory, bool isoformat)
        {
            this.WriteSummaryHeader("EXPRESS Specification", false);
            this.m_writer.WriteLine("<div class=\"express\"><code class=\"express\">");

            // new: comment tag
            if (isoformat)
            {
                this.WriteExpressLine(0, "*)");
            }

            this.WriteExpressEntity(entity);

            // Comment tag for ISO STEP documentation requirements
            if (isoformat)
            {
                this.WriteExpressLine(0, "(*");
            }

            // link to EXPRESS-G
            WriteExpressDiagram(entity);

            this.m_writer.WriteLine("</code></div>");


            if (isoformat)
            {
                // inheritance
                this.m_writer.WriteLine();
                this.m_writer.WriteLine("<p class=\"spec-head\">Inheritance Graph:</p>");

                this.m_writer.WriteLine("<span class=\"express\">");

                this.WriteExpressLine(0, BEGIN_KEYWORD + "ENTITY" + END_KEYWORD + " " + entity.Name);

                WriteExpressInheritance(entity, entity);

                this.WriteExpressLine(0, BEGIN_KEYWORD + "END_ENTITY;" + END_KEYWORD);

                this.m_writer.WriteLine("</span>");
            }

            this.WriteSummaryFooter();
        }

        public void WriteExpressEntity(DocEntity entity)
        {
            // build up list of subtypes from other schemas
            SortedList<string, DocEntity> subtypes = new SortedList<string, DocEntity>(this); // use custom comparer to match with Visual Express ordering

            // include from other schemas
            foreach (DocObject eachdoc in this.m_mapEntity.Values)
            {
                if (eachdoc is DocEntity)
                {
                    DocEntity eachent = (DocEntity)eachdoc;
                    if (eachent.BaseDefinition != null && eachent.BaseDefinition.Equals(entity.Name))
                    {
                        subtypes.Add(eachent.Name, eachent);
                    }
                }
            }

            string noteAbstract = null;
            string termEntity = "<b>;</b>";
            string termSuper = "<b>;</b>";            
            if ((entity.EntityFlags & 0x20) == 0)
            {
                noteAbstract = BEGIN_KEYWORD + "ABSTRACT" + END_KEYWORD + " ";
                termEntity = null;
            }
            if (subtypes.Count > 0 || (entity.Subtypes != null && entity.Subtypes.Count > 0))
            {
                termEntity = null;
            }
            if (entity.BaseDefinition != null)
            {
                termEntity = null;
                termSuper = null;
            }
             
            this.WriteExpressLine(0, BEGIN_KEYWORD + "ENTITY" + END_KEYWORD + " " + entity.Name + termEntity);
            if((entity.EntityFlags & 0x20) == 0 || subtypes.Count > 0 || (entity.Subtypes != null && entity.Subtypes.Count > 0))
            {                
                if (subtypes.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();

                    // Capture all subtypes, not just those within schema
                    int countsub = 0;
                    foreach (string ds in subtypes.Keys)
                    {
                        DocEntity refent = subtypes[ds];
                        if (this.m_included == null || this.m_included.ContainsKey(refent))
                        {
                            countsub++;

                            if (sb.Length != 0)
                            {
                                sb.Append(", ");
                            }

                            sb.Append(this.FormatDefinition(ds));
                        }
                    }

                    if (countsub > 1)
                    {
                        this.WriteExpressLine(1, noteAbstract + BEGIN_KEYWORD + "SUPERTYPE OF" + END_KEYWORD + "(" + BEGIN_KEYWORD + "ONEOF" + END_KEYWORD + "(" + sb.ToString() + "))" + termSuper);
                    }
                    else if(countsub == 1)
                    {
                        this.WriteExpressLine(1, noteAbstract + BEGIN_KEYWORD + "SUPERTYPE OF" + END_KEYWORD + "(" + sb.ToString() + ")" + termSuper);
                    }
                }
                else
                {
                    this.WriteExpressLine(1, noteAbstract + BEGIN_KEYWORD + "SUPERTYPE" + END_KEYWORD + termSuper);
                }
            }

            if(entity.BaseDefinition != null)
            {
                this.WriteExpressLine(1, BEGIN_KEYWORD + "SUBTYPE OF" + END_KEYWORD + " (" + FormatDefinition(entity.BaseDefinition) + ")<b>;</b>");
            }

            WriteExpressAttributes(entity, null);

            if (entity.UniqueRules != null && entity.UniqueRules.Count > 0)
            {
                this.WriteExpressLine(1, BEGIN_KEYWORD + "UNIQUE" + END_KEYWORD);

                this.WriteExpressHeader(2);
                foreach (DocUniqueRule docWhere in entity.UniqueRules)
                {
                    this.m_writer.Write("&nbsp;&nbsp;");
                    this.m_writer.Write(docWhere.Name);

                    this.m_writer.Write(" : ");
                    foreach (DocUniqueRuleItem item in docWhere.Items)
                    {
                        if (item != docWhere.Items[0])
                        {
                            this.m_writer.Write(", ");
                        }
                        this.m_writer.Write(item.Name);
                    }
                    this.m_writer.Write(";<br/>");
                }

                this.WriteExpressFooter();
            }

            if (entity.WhereRules != null && entity.WhereRules.Count > 0)
            {
                this.WriteExpressLine(1, BEGIN_KEYWORD + "WHERE" + END_KEYWORD);

                this.WriteExpressHeader(2);
                foreach (DocWhereRule docWhere in entity.WhereRules)
                {
                    string escaped = System.Security.SecurityElement.Escape(docWhere.Expression);
                    if (escaped != null)
                    {
                        escaped = escaped.Replace("&apos;", "'");
                    }

                    this.m_writer.Write("&nbsp;&nbsp;");
                    this.m_writer.Write(docWhere.Name);
                    this.m_writer.Write(" : " + escaped + ";<br/>");                    
                }

                this.WriteExpressFooter();
            }

            WriteExpressLine(0, BEGIN_KEYWORD + "END_ENTITY;" + END_KEYWORD);

        }

        public void WriteExpressDiagram(DocDefinition docDef)
        {
            int diagramnumber = docDef.DiagramNumber;
            string schema = null;
            if (this.m_mapSchema.TryGetValue(docDef.Name, out schema))
            {

                // Per ISO doc requirement, use icon to link to diagram
                this.m_writer.WriteLine(
                    "<p class=\"std\">" +
                    "<a href=\"../../../annex/annex-d/" + schema.ToLower() + "/diagram_" + diagramnumber.ToString("D4") +
                    ".htm\" ><img src=\"../../../img/diagram.png\" style=\"border: 0px\" title=\"Link to EXPRESS-G diagram\" alt=\"Link to EXPRESS-G diagram\">&nbsp;EXPRESS-G diagram</a>" +
                    "</p>");
            }
        }

        // recurses up inheritance chain
        private void WriteExpressInheritance(DocEntity entity, DocEntity treeleaf)
        {
            if (entity.BaseDefinition != null)
            {
                if (this.m_mapEntity.ContainsKey(entity.BaseDefinition))
                {
                    DocEntity baseEntity = (DocEntity)this.m_mapEntity[entity.BaseDefinition];
                    WriteExpressInheritance(baseEntity, treeleaf);
                }
            }

            WriteExpressLine(1, BEGIN_KEYWORD + "ENTITY" + END_KEYWORD + " " + FormatDefinition(entity.Name));
            WriteExpressAttributes(entity, treeleaf);
        }

        public void WriteExpressTypeAndDocumentation(DocType type, bool suppresshistory, bool isoformat)
        {
            this.WriteSummaryHeader("EXPRESS Specification", false);
            this.m_writer.WriteLine("<div class=\"express\"><code class=\"express\">");

            this.WriteExpressHeader(2);

            // Per ISO doc requirement, comment tag
            if (isoformat)
            {
                this.WriteExpressLine(0, "*)");
            }

            WriteExpressType(type);

            if (isoformat)
            {
                this.WriteExpressLine(0, "(*");
            }
            WriteExpressFooter();

            this.m_writer.WriteLine("</p>");

            // link to EXPRESS-G
            WriteExpressDiagram(type);

            this.m_writer.WriteLine("</code></div>");
            this.WriteSummaryFooter();

        }

        public void WriteExpressType(DocType type)
        {
            if (type is DocEnumeration)
            {
                DocEnumeration docenum = (DocEnumeration)type;

                this.WriteExpressLine(0, BEGIN_KEYWORD + "TYPE" + END_KEYWORD + " " + docenum.Name + " = " + BEGIN_KEYWORD + "ENUMERATION OF" + END_KEYWORD + " (");                
                this.WriteExpressHeader(2);

                if (docenum.Constants != null)
                {
                    foreach (DocConstant docconst in docenum.Constants)
                    {
                        this.m_writer.Write("&nbsp;");
                        this.m_writer.Write(docconst.Name);

                        if (docconst == docenum.Constants[docenum.Constants.Count - 1])
                        {
                            this.m_writer.Write(");<br/>");
                        }
                        else
                        {
                            this.m_writer.Write(", <br/>");
                        }
                    }
                }

                this.WriteExpressFooter();
            }
            else if (type is DocSelect)
            {
                DocSelect docenum = (DocSelect)type;

                this.WriteExpressLine(0, BEGIN_KEYWORD + "TYPE" + END_KEYWORD + " " + docenum.Name + " = " + BEGIN_KEYWORD + "SELECT" + END_KEYWORD + " (");
                this.WriteExpressHeader(2);

                if (docenum.Selects != null)
                {
                    bool previtem = false;
                    foreach (DocSelectItem docconst in docenum.Selects)
                    {
                        DocObject docref = null;
                        if (this.m_mapEntity.TryGetValue(docconst.Name, out docref))
                        {
                            if (this.m_included == null || this.m_included.ContainsKey(docref))
                            {
                                if (previtem)
                                {
                                    this.m_writer.Write(", <br/>");
                                }

                                this.m_writer.Write("&nbsp;");
                                this.m_writer.Write(this.FormatDefinition(docconst.Name));

                                previtem = true;
                            }
                        }
                    }

                    if (previtem)
                    {
                        this.m_writer.Write(");<br/>");
                    }
                }

                this.WriteExpressFooter();
            }
            else if (type is DocDefined)
            {
                DocDefined docenum = (DocDefined)type;

                string length = "";
                if (docenum.Length > 0)
                {
                    length = " (" + docenum.Length.ToString() + ")";
                }
                else if (docenum.Length < 0)
                {
                    int len = -docenum.Length;
                    length = " (" + len.ToString() + ") FIXED";
                }

                WriteExpressHeader(0);
                this.m_writer.Write(BEGIN_KEYWORD + "TYPE" + END_KEYWORD + " " + docenum.Name + " = ");

                if (docenum.Aggregation != null)
                {
                    this.WriteExpressAggregation(docenum.Aggregation);
                }

                this.m_writer.Write(FormatDefinition(docenum.DefinedType) + length + ";<br/>");
                WriteExpressFooter();

                this.WriteExpressHeader(2);
                if (docenum.WhereRules != null && docenum.WhereRules.Count > 0)
                {
                    this.WriteExpressLine(1, BEGIN_KEYWORD + "WHERE" + END_KEYWORD);

                    this.WriteExpressHeader(2);
                    foreach (DocWhereRule docWhere in docenum.WhereRules)
                    {
                        string escaped = System.Security.SecurityElement.Escape(docWhere.Expression);
                        escaped = escaped.Replace("&apos;", "'");

                        this.m_writer.Write("&nbsp;&nbsp;");
                        this.m_writer.Write(docWhere.Name);
                        this.m_writer.Write(" : " + escaped + "<br/>");
                    }

                    this.WriteExpressFooter();
                }

                this.WriteExpressFooter();
            }

            WriteExpressLine(0, BEGIN_KEYWORD + "END_TYPE;" + END_KEYWORD);
        }

        public void WriteExpressFunction(DocFunction entity)
        {
            this.WriteLine("<p>");
            this.WriteLine(BEGIN_KEYWORD + "FUNCTION" + END_KEYWORD + " " + entity.Name + "<br/>\r\n");

            string escaped = entity.Expression;
            escaped = System.Security.SecurityElement.Escape(escaped);
            escaped = escaped.Replace("&apos;", "'");

            escaped = escaped.Replace("\r\n", "<br/>");
            escaped = escaped.Replace("    ", "&nbsp;&nbsp;&nbsp;&nbsp;");
            escaped = escaped.Replace("   ", "&nbsp;&nbsp;&nbsp;");
            escaped = escaped.Replace("  ", "&nbsp;&nbsp;");

            this.WriteLine(escaped);

            this.WriteLine("<br/>\r\n");
            this.WriteLine(BEGIN_KEYWORD + "END_FUNCTION" + END_KEYWORD + ";\r\n");
            this.WriteLine("</p>\r\n");
        }

        public void WriteExpressGlobalRule(DocGlobalRule entity)
        {
            this.WriteLine("<p>");
            this.WriteLine(BEGIN_KEYWORD + "RULE" + END_KEYWORD + " " + entity.Name + " <b>FOR</b> (");
            this.WriteDefinition(entity.ApplicableEntity);
            this.WriteLine(");<br/>\r\n");

            if (!String.IsNullOrEmpty(entity.Expression))
            {
                string escaped = entity.Expression;
                escaped = System.Security.SecurityElement.Escape(escaped);
                escaped = escaped.Replace("&apos;", "'");

                escaped = escaped.Replace("\r\n", "<br/>");
                escaped = escaped.Replace("    ", "&nbsp;&nbsp;&nbsp;&nbsp;");
                escaped = escaped.Replace("   ", "&nbsp;&nbsp;&nbsp;");
                escaped = escaped.Replace("  ", "&nbsp;&nbsp;");

                this.WriteLine(escaped);

                this.WriteLine("<br/>");
            }

            if (entity.WhereRules != null && entity.WhereRules.Count > 0)
            {
                this.WriteLine("&nbsp;&nbsp;&nbsp;&nbsp;" + BEGIN_KEYWORD + "WHERE" + END_KEYWORD + "<br/>\r\n");
                foreach (DocWhereRule docWhere in entity.WhereRules)
                {
                    string escaped = System.Security.SecurityElement.Escape(docWhere.Expression);
                    escaped = escaped.Replace("&apos;", "'");// must unescape for HTML (instead of XML)
                    this.WriteLine("&nbsp;&nbsp;&nbsp;&nbsp;");
                    this.WriteLine(docWhere.Name);
                    this.WriteLine(" : " + escaped + "<br/>\r\n");
                }
            }

            this.WriteLine(BEGIN_KEYWORD + "END_RULE" + END_KEYWORD + ";");

            this.WriteLine("</p>");
        }

        /// <summary>
        /// Writes alphabetical listing according to a specific locale
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="caption">The caption to use, e.g. "Entities"</param>
        /// <param name="locale">The locale for which to generate listings or NULL for default locale.</param>
        public void WriteLocalizedListing<T>(string caption, string locale, string path, string name) where T : DocObject
        {
            SortedList<string, T> alphaMissing = new SortedList<string, T>();

            int count = 0;
            SortedList<string, T> alphaEntity = new SortedList<string, T>();
            foreach (string s in this.m_mapEntity.Keys)
            {
                DocObject obj = this.m_mapEntity[s];
                if (obj is T && (this.m_included == null || this.m_included.ContainsKey(obj)))
                {
                    count++;

                    if(locale == null)
                    {
                        // default locale (IFC4 uses British English as default locale)
                        alphaEntity.Add(obj.Name, (T)obj);
                    }
                    else if(obj.Localization != null)
                    {
                        bool exists = false;

                        // find specific locale
                        foreach(DocLocalization docLocal in obj.Localization)
                        {
                            if (docLocal.Locale != null && docLocal.Locale.Equals(locale, StringComparison.OrdinalIgnoreCase) && docLocal.Name != null && !alphaEntity.ContainsKey(docLocal.Name))
                            {
                                // found it
                                alphaEntity.Add(docLocal.Name, (T)obj);
                                exists = true;
                                break;
                            }
                        }

                        if (!exists)
                        {
                            //System.Diagnostics.Debug.WriteLine("[" + locale + "] Missing transation: " + obj.Name);
                            alphaMissing.Add(obj.Name, (T)obj);
                        }
                    }
                }
            }

            if (locale == null)
            {
                locale = "en"; // default
            }

            string localeheader = locale.ToUpper();
            System.Globalization.CultureInfo cultureinfo = System.Globalization.CultureInfo.GetCultureInfo(locale);
            if (cultureinfo != null)
            {
                localeheader += " [" + cultureinfo.EnglishName + "]";
            }

            this.WriteHeader(localeheader, 3);
            this.WriteLine("<h2 class=\"annex\">" + caption + " (" + alphaEntity.Count.ToString() + " translations out of " + count + ")</h2>");
            this.WriteLine("<ul class=\"std\">");

            foreach (string key in alphaEntity.Keys)
            {
                T entity = alphaEntity[key];

                string schema = this.m_mapSchema[entity.Name];

                string hyperlink = null;
                if (entity is DocPropertySet)
                {
                    hyperlink = @"../../../schema/" + schema.ToLower() + @"/pset/" + entity.Name.ToLower() + ".htm";
                }
                else if (entity is DocQuantitySet)
                {
                    hyperlink = @"../../../schema/" + schema.ToLower() + @"/qset/" + entity.Name.ToLower() + ".htm";
                }
                else
                {
                    hyperlink = @"../../../schema/" + schema.ToLower() + @"/lexical/" + entity.Name.ToLower() + ".htm";
                }
                this.WriteLine("<li><a class=\"listing-link\" href=\"" + hyperlink + "\">" + key + "</a></li>\r\n");
            }

            this.WriteLine("</ul>");

            if (alphaMissing.Count > 0)
            {
                this.WriteLine("<p><i>The following definitions do not have translations for this locale: ");
                foreach (string key in alphaMissing.Keys)
                {
                    this.WriteLine(key + "; ");
                }
                this.WriteLine("</i></p>");
            }

            string linkid = locale.Substring(0,2) + "-alphabeticalorder-" + caption.ToLower().Replace(' ', '-');
            this.WriteLinkTo(linkid, 3);


            this.WriteFooter(Properties.Settings.Default.Footer);

            using (FormatHTM htmLink = new FormatHTM(path + "/link/" + linkid + ".htm", this.m_mapEntity, this.m_mapSchema, this.m_included))
            {
                htmLink.WriteLinkPage("../annex/annex-b/" + locale.Substring(0,2).ToLower() + "/alphabeticalorder_" + name + ".htm");
            }
        }

        public void WriteAlphabeticalListing<T>(string caption, string path, string name) where T : DocObject
        {
            SortedList<string, T> alphaEntity = new SortedList<string, T>();
            foreach (string s in this.m_mapEntity.Keys)
            {
                DocObject obj = this.m_mapEntity[s];
                if (obj is T && (this.m_included == null || this.m_included.ContainsKey(obj)))
                {
                    alphaEntity.Add(s, (T)obj);
                }
            }

            this.WriteHeader("Alphabetical Listing", -2, -1, -1, -1, Properties.Settings.Default.Header);
            this.WriteLine("<h2 class=\"annex\">" + caption + " (" + alphaEntity.Count.ToString() + ")</h2>");
            this.WriteLine("<ul class=\"std\">");

            foreach (string key in alphaEntity.Keys)
            {
                T entity = alphaEntity[key];

                string schema = this.m_mapSchema[entity.Name];

                string hyperlink = null;
                if (entity is DocPropertySet)
                {
                    hyperlink = @"../../schema/" + schema.ToLower() + @"/pset/" + entity.Name.ToLower() + ".htm";
                }
                else if (entity is DocQuantitySet)
                {
                    hyperlink = @"../../schema/" + schema.ToLower() + @"/qset/" + entity.Name.ToLower() + ".htm";
                }
                else
                {
                    hyperlink = @"../../schema/" + schema.ToLower() + @"/lexical/" + entity.Name.ToLower() + ".htm";
                }
                this.WriteLine("<li><a class=\"listing-link\" href=\"" + hyperlink + "\">" + entity.Name + "</a></li>\r\n");
            }

            this.WriteLine("</ul>");

            string linkid = "alphabeticalorder-" + caption.ToLower().Replace(' ', '-');
            this.WriteLinkTo(linkid, 2);
            this.WriteFooter(Properties.Settings.Default.Footer);

            using (FormatHTM htmLink = new FormatHTM(path + "/link/" + linkid + ".htm", this.m_mapEntity, this.m_mapSchema, this.m_included))
            {
                htmLink.WriteLinkPage("../annex/annex-b/alphabeticalorder_" + name.ToLower() + ".htm");
            }
        }

        public void WriteInheritanceMapping(DocProject docProject, DocModelView[] views)
        {
            SortedList<string, DocEntity> alphaEntity = new SortedList<string, DocEntity>();
            foreach (string s in this.m_mapEntity.Keys)
            {
                DocObject obj = this.m_mapEntity[s];
                if (obj is DocEntity)
                {
                    alphaEntity.Add(s, (DocEntity)obj);
                }
            }


            this.WriteHeader("Inheritance Listing", 3);
            this.WriteLine("<h2 class=\"annex\">" + "Mappings" + "</h2>");
            this.WriteLine("<table class=\"gridtable\">");

            this.WriteLine("<tr>");
            this.WriteLine("<th>Entity</th>");

            if (views != null)
            {
                Dictionary<DocObject, bool>[] maps = new Dictionary<DocObject, bool>[views.Length];
                for (int iView = 0; iView < views.Length; iView++)
                {
                    maps[iView] = new Dictionary<DocObject, bool>();
                    DocModelView docView = views[iView];
                    docProject.RegisterObjectsInScope(docView, maps[iView]);

                    this.WriteLine("<th>" + docView.Code + "</th>");
                }
                this.WriteLine("</tr>");

                WriteInheritanceMappingLevel(null, alphaEntity.Values, maps, 0);
            }
            this.WriteLine("</table>");

            this.WriteFooter(Properties.Settings.Default.Footer);
        }

        public void WriteTemplateTable(DocProject docProject, DocTemplateDefinition docTemplateDefinition, int level, Dictionary<DocObject, bool>[] dictionaryViews)
        {
            bool isTemplateUsed = false;

            string templateUrl = "../schema/templates/" + DocumentationISO.MakeLinkName(docTemplateDefinition) + ".htm";
            string templateAnchor = "<a href=\"" + templateUrl + "\">" + docTemplateDefinition.Name + "</a>";

            StringBuilder sB = new StringBuilder();

            for (int j = 0; j < dictionaryViews.Length; j++)
            {
                Dictionary<DocObject, bool> dictionary = dictionaryViews[j];
                if (dictionary != null)
                {
                    sB.Append("<td>");
                    if (dictionary.ContainsKey(docTemplateDefinition))
                    {
                        sB.Append("X");
                        isTemplateUsed = true;
                    }
                    sB.Append("</td>");
                }
            }

            if (dictionaryViews != null && (!isTemplateUsed || !String.IsNullOrEmpty(docTemplateDefinition.Code))) // new: don't display unused templates - only adds clutter
                return;

            this.WriteLine("<tr><td>");
            for (int i = 0; i < level; i++)
            {
                this.WriteLine("&nbsp;");
            }

            if (isTemplateUsed)
            {
                this.WriteLine(templateAnchor);
            }
            else
            {
                this.WriteLine(docTemplateDefinition.Name);
            }
            this.WriteLine("</td>");
            this.WriteLine(sB.ToString());

            this.WriteLine("</tr>");
            foreach (DocTemplateDefinition childTemplateDefinition in docTemplateDefinition.Templates)
            {
                WriteTemplateTable(docProject, childTemplateDefinition, level + 1, dictionaryViews);
            }
        }

        private void WriteInheritanceMappingLevel(string baseclass, IList<DocEntity> list, Dictionary<DocObject, bool>[] maps, int indent)
        {
            bool include;
            foreach (DocEntity entity in list)
            {
                if (entity.BaseDefinition == baseclass)
                {
                    string schema = this.m_mapSchema[entity.Name];
                    string hyperlink = @"../schema/" + schema.ToLower() + @"/lexical/" + entity.Name.ToLower() + ".htm";

                    this.Write("<tr><td>");

                    for (int i = 0; i < indent; i++ )
                    {
                        this.Write("&nbsp;&nbsp;");
                    }

                    bool includeitem = false;
#if false // temp
                    for (int iView = 0; iView < maps.Length; iView++)
                    {
                        if(maps[iView].TryGetValue(entity, out include) && include)
                        {
                            includeitem = true;
                            break;
                        }
                    }
#endif

                    if (includeitem)
                    {
                        this.Write("<a class=\"listing-link\" href=\"" + hyperlink + "\">");
                    }
                    this.Write(entity.Name);
                    if(includeitem)
                    {
                        this.Write("</a>");
                    }
                    this.Write("</td>");

                    for (int iView = 0; iView < maps.Length; iView++ )
                    {
                        this.Write("<td>");
                        if(maps[iView].TryGetValue(entity, out include) && include)
                        {
                            this.Write("X");
                        }
                        this.Write("</td>");
                    }

                    this.WriteLine("</tr>");

                    // recurse
                    WriteInheritanceMappingLevel(entity.Name, list, maps, indent + 1);

                    this.WriteLine("</ul></li>\r\n");
                }
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="baseclass">Name of base class or null for all entities</param>
        public void WriteInheritanceListing(string baseclass, bool predefined, string caption, DocModelView docView, string path, string filename)
        {
            SortedList<string, DocEntity> alphaEntity = new SortedList<string, DocEntity>();
            foreach (string s in this.m_mapEntity.Keys)
            {
                DocObject obj = this.m_mapEntity[s];
                if (obj is DocEntity && (this.m_included == null || this.m_included.ContainsKey(obj)))
                {
                    alphaEntity.Add(s, (DocEntity)obj);
                }
            }

            this.WriteHeader("Inheritance Listing", 3);
            this.WriteLine("<h2 class=\"annex\">" + caption + "</h2>");
            this.WriteLine("<ul class=\"std\">");

            WriteInheritanceLevel(baseclass, alphaEntity.Values, predefined);

            this.WriteLine("</ul>");

            string linkid = "inheritance-" + DocumentationISO.MakeLinkName(docView) + "-" + caption.ToLower();
            this.WriteLinkTo(linkid, 3);
            this.WriteFooter(Properties.Settings.Default.Footer);

            using (FormatHTM htmLink = new FormatHTM(path + "/link/" + linkid + ".htm", this.m_mapEntity, this.m_mapSchema, this.m_included))
            {
                htmLink.WriteLinkPage("../annex/annex-c/" + DocumentationISO.MakeLinkName(docView) + "/" + filename + ".htm");
            }
        }

        private void WriteInheritanceLevel(string baseclass, IList<DocEntity> list, bool predefined)
        {
            foreach (DocEntity entity in list)
            {
                if (entity.BaseDefinition == baseclass)
                {
                    string schema = this.m_mapSchema[entity.Name];
                    string hyperlink = @"../../../schema/" + schema.ToLower() + @"/lexical/" + entity.Name.ToLower() + ".htm";

                    this.Write("<li><a class=\"listing-link\" href=\"" + hyperlink + "\">" + entity.Name + "</a>");
                    
                    if(predefined && !baseclass.EndsWith("Type") && this.m_mapEntity.ContainsKey(entity.Name + "Type"))
                    {
                        DocEntity entType = this.m_mapEntity[entity.Name + "Type"] as DocEntity;
                        if (entType != null && list.Contains(entType))
                        {
                            this.Write(" - <a class=\"listing-link\" href=\"../../../schema/" + schema.ToLower() + @"/lexical/" + entity.Name.ToLower() + "type.htm\">" + entity.Name + "Type</a>");
                        }
                    }

                    this.WriteLine("<ul>");

                    if (predefined)
                    {
                        foreach (DocAttribute attr in entity.Attributes)
                        {
                            if (attr.Name == "PredefinedType")
                            {
                                DocEnumeration docEnum = (DocEnumeration)this.m_mapEntity[attr.DefinedType];
                                this.WriteLine("<table class=\"inheritanceenum\">");
                                foreach (DocConstant docConst in docEnum.Constants)
                                {
                                    this.WriteLine("<tr><td>" + docConst.Name + "</td></tr>");
                                }
                                this.WriteLine("</table>");
                                break;
                            }
                        }
                    }

                    // recurse
                    WriteInheritanceLevel(entity.Name, list, predefined);

                    this.WriteLine("</ul></li>\r\n");
                }
            }
        }

        /// <summary>
        /// Writes ISO documentation by formatting hyperlinks and removing blocks starting with "HISTORY" and "IFC2X4 CHANGE"
        /// </summary>
        /// <param name="content"></param>
        public void WriteDocumentationForISO(string content, DocObject current, bool suppresshistory)
        {
            if (content == null)
                return;

            // strip off "<EPM-HTML>" and "</EPM-HTML>"
            int iHead = content.IndexOf("<EPM-HTML>", StringComparison.OrdinalIgnoreCase);
            int iTail = content.LastIndexOf("</EPM-HTML>", StringComparison.OrdinalIgnoreCase);
            if (iHead >= 0 && iTail >= 0)
            {
                content = content.Substring(iHead + 10, iTail - iHead - 11);
            }

            // strip off "Definition from IAI"
            
            content = content.Replace("<u>Definition from IAI</u>:", "");
            content = content.Replace("<U>Definition from IAI</U>:", "");
            content = content.Replace("<u>Definition from IAI:</u>", "");
            content = content.Replace("<U>Definition from IAI:</U>", "");
            content = content.Replace("<i>Definition from IAI</i>:", "");
            content = content.Replace("<u><b>Definition from IAI</b></u>:", "");
            content = content.Replace("<b><u>Definition from IAI</u></b>:", "");
            content = content.Replace("Definition from IAI:", "");

            // target="SOURCE" -> target="info" (for transition; need to update vex)
            content = content.Replace("target=\"SOURCE\"", "target=\"info\"");

            int index = 0;

            // force pset and qset links to lowercase (for Linux servers)
            while (index >= 0)
            {
                index = content.IndexOf("/psd/", index, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    int tail = content.IndexOf("\"", index);
                    if (tail >= 0)
                    {
                        string url = content.Substring(index, tail - index);
                        url = url.Replace("/psd/", "/");
                        url = url.Replace("/Pset_", "/pset/pset_");
                        url = url.Replace(".xml", ".htm");
                        url = url.ToLower();
                        
                        content = content.Substring(0, index) + url + content.Substring(tail);
                    }

                    index++;
                }
            }

            index = 0;
            while (index >= 0)
            {
                index = content.IndexOf("/qto/", index, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    int tail = content.IndexOf("\"", index);
                    if (tail >= 0)
                    {
                        string url = content.Substring(index, tail - index);
                        url = url.Replace("/qto/", "/");
                        url = url.Replace("/Qto_", "/qset/qto_");
                        url = url.Replace(".xml", ".htm");
                        url = url.ToLower();

                        content = content.Substring(0, index) + url + content.Substring(tail);
                    }

                    index++;
                }
            }

            if (suppresshistory)
            {
                // remove history and deprecation info
                index = 0;
                while (index >= 0)
                {
                    index = content.IndexOf("<blockquote", index, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        int end = content.IndexOf("</blockquote>", index, StringComparison.OrdinalIgnoreCase);
                        if (end > index)
                        {
                            string block = content.Substring(index, end - index + 13);
                            if (block.Contains("HISTORY") ||
                                block.Contains("IFC2X") ||
                                block.Contains("IFC2x") ||
                                block.Contains("IFC 2X") ||
                                block.Contains("IFC 2x") ||
                                block.Contains("IFC4"))
                            {
                                // exclude the block
                                content = content.Substring(0, index) + content.Substring(end + 13, content.Length - end - 13);
                            }                            
                            else
                            {
                                index = end;
                            }
                        }
                        else
                        {
                            index++; // badly formatted documentation; no end block
                        }
                    }
                }

                // remove excluded sections for ISO (may include nested blocks)
                index = 0;
                while (index >= 0 && index < content.Length)
                {
                    index = content.IndexOf("<blockquote class=\"extDef\">", index, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        int end = FindEndOfTag(content, index);
                        if (end >= 0)
                        {
                            content = content.Substring(0, index) + content.Substring(end);
                        }
                        index++;
                    }
                }
            }

            // convert <em> to <i> -- prepare for links
            content = content.Replace("<em>", "<i>");
            content = content.Replace("</em>", "</i>");

            // links
            index = 0;
            while (index >= 0)
            {
                string prefix = "";// was "Ifc";
                int prelen = prefix.Length;

                index = content.IndexOf("<i>" + prefix, index, StringComparison.OrdinalIgnoreCase); // capture text, but not any hyperlink surrounding it
                if (index >= 0)
                {
                    int end = content.IndexOf("</i>", index, StringComparison.OrdinalIgnoreCase);
                    if (end > index)
                    {
                        string block = content.Substring(index, end - index + prelen + 4);
                        string def = content.Substring(index + prelen + 3, end - index - prelen - 3);

                        if(def.StartsWith("Pset_"))
                        {
                            this.ToString();
                        }

                        DocObject docDef = null;
                        if (this.m_mapEntity.TryGetValue(def, out docDef) && (this.m_included == null || this.m_included.ContainsKey(docDef)))
                        {
                            // IFC definition exists; 
                            if (current is DocSchema || current is DocTemplateDefinition || current is DocSection)
                            {
                                string schema = null;
                                if (def.StartsWith(prefix) && this.m_mapSchema.TryGetValue(def, out schema))
                                {
                                    // only 1 level up
                                    string hyperlink = schema.ToLower() + @"/lexical/" + def.ToLower() + ".htm";
                                    if (!(current is DocSection))
                                    {
                                        hyperlink = "../" + hyperlink;
                                    }
                                    string format = "<a href=\"" + hyperlink + "\">" + def + "</a>";
                                    content = content.Substring(0, index) + format + content.Substring(end + 4);
                                }
                                else if (docDef is DocTemplateDefinition)
                                {
                                    string hyperlink = def.ToLower().Replace(' ', '-') + ".htm";
                                    if (current is DocSection)
                                    {
                                        hyperlink = "templates/" + hyperlink;
                                    }
                                    string format = "<a href=\"" + hyperlink + "\">" + def + "</a>";
                                    content = content.Substring(0, index) + format + content.Substring(end + 4);
                                }
                                else
                                {
                                    // leave as italics
                                    index++;
                                }
                            }
                            else if(current is DocChangeSet)
                            {
                                string schema = null;
                                if (def.StartsWith(prefix) && this.m_mapSchema.TryGetValue(def, out schema))
                                {
                                    string hyperlink = @"../../../schema/" + schema.ToLower() + @"/lexical/" + def.ToLower() + ".htm";
                                    string format = "<a href=\"" + hyperlink + "\">" + def + "</a>";
                                    content = content.Substring(0, index) + format + content.Substring(end + 4);
                                }
                            }
                            else if (current is DocExample)
                            {
                                string schema = null;
                                if (def.StartsWith(prefix) && this.m_mapSchema.TryGetValue(def, out schema))
                                {                                    
                                    string hyperlink = @"../../schema/" + schema.ToLower() + @"/lexical/" + def.ToLower() + ".htm";
                                    string format = "<a href=\"" + hyperlink + "\">" + def + "</a>";
                                    content = content.Substring(0, index) + format + content.Substring(end + 4);
                                }
                            }
                            else if (docDef != current)
                            {
                                // replace it with hyperlink
                                string format = this.FormatDefinition(def);
                                content = content.Substring(0, index) + format + content.Substring(end + 4);
                            }
                            else
                            {
                                // new: use self-ref
                                string format = "<span class=\"self-ref\">" + docDef + "</span>";
                                content = content.Substring(0, index) + format + content.Substring(end + 4);

                                // leave as italics
                                index += format.Length;
                            }
                        }
                        else
                        {
                            index++; // non-existant or misspelled IFC reference
                        }
                    }
                    else
                    {
                        index++; // bad format
                    }

                }

            }
             
            // make all image file links lowercase
            index = 0;
            while (index >= 0)
            {
                index = content.IndexOf("src=\"figures/", index, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    int tail = content.IndexOf("\"", index + 14);
                    if (tail >= 0)
                    {
                        string format = content.Substring(index + 13, tail - index - 13);
                        format = format.ToLower();
                        content = content.Substring(0, index + 13) + format + content.Substring(tail);
                    }

                    index++;
                }                
            }

            this.m_writer.Write(content);
        }

        /// <summary>
        /// Returns the position after the end of a tag; expects separate opening and closing tags; deals with nested tags.
        /// Expects less-than and greater-then symbols to be ONLY used for tags (escaped properly if used in strings)
        /// </summary>
        /// <param name="content">String to search</param>
        /// <param name="start">Starting index at the beginning of the tag; the opening bracket.</param>
        /// <returns>Index after the closing bracket, or -1 if no closing bracket (bad html)</returns>
        private static int FindEndOfTag(string content, int start)
        {
            bool parsetag = false;
            int directive = 0; // 0 = inline tag; +1 = open tag; -1 = close tag
            
            int nest = 0;
            for (int index = start; index < content.Length; index++)
            {
                char ch = content[index];
                if (!parsetag && ch == '<')
                {
                    parsetag = true;
                    directive = 1; // assume inner tags unless slash exists otherwise
                }
                else if (parsetag && ch == '/')
                {
                    if (content[index - 1] == '<')
                    {
                        directive = -1; // close previous tag
                    }
                    else
                    {
                        directive = 0; // same tag
                    }
                }
                else if (parsetag && ch == '>')
                {
                    // implicit closing for special tags such as <br>
                    if (index >= 3 &&
                        content[index - 3] == '<' &&
                        content[index - 2] == 'b' &&
                        content[index - 1] == 'r')
                    {
                        directive = 0;
                    }

                    parsetag = false;

                    nest += directive;
                    if (nest == 0)
                    {
                        return index + 1;
                    }
                }
            }

            return -1; // no closing tag
        }

        internal void WriteScript(int iSection, int iSchema, int iType, int iItem)
        {
            if (iSection < 0)
            {
                // annex
                char chAnnex = (char)('A' - iSection - 1);

                if (iSchema == 0)
                {
                    this.WriteLine(
                        "\r\n" +
                        "<script type=\"text/javascript\">\r\n" +
                        "<!--\r\n" +
                        "    parent.index.location.replace(\"toc-" + chAnnex.ToString().ToLower() + ".htm\");\r\n" +
                        "//-->\r\n" +
                        "</script>\r\n");
                }
                else if (chAnnex == 'A' || (chAnnex == 'D' && iSchema == -1) || (chAnnex == 'D' && iSchema == 1 && iType > 0) || (chAnnex == 'D' && iSchema == 2) || chAnnex == 'F')
                {
                    // 2 levels up
                    this.WriteLine(
                        "\r\n" +
                        "<script type=\"text/javascript\">\r\n" +
                        "<!--\r\n" +
                        "    parent.index.location.replace(\"../../toc-" + chAnnex.ToString().ToLower() + ".htm\");\r\n" +
                        "//-->\r\n" +
                        "</script>\r\n");
                }
                else if (chAnnex == 'E')
                {
                    // 1 level up
                    this.WriteLine(
                        "\r\n" +
                        "<script type=\"text/javascript\">\r\n" +
                        "<!--\r\n" +
                        "    parent.index.location.replace(\"../toc-" + chAnnex.ToString().ToLower() + ".htm#" + iSchema.ToString() + "\");\r\n" +
                        "//-->\r\n" +
                        "</script>\r\n");
                }
                else
                {
                    // 1 level up
                    this.WriteLine(
                        "\r\n" +
                        "<script type=\"text/javascript\">\r\n" +
                        "<!--\r\n" +
                        "    parent.index.location.replace(\"../toc-" + chAnnex.ToString().ToLower() + ".htm\");\r\n" +
                        "//-->\r\n" +
                        "</script>\r\n");
                }
            }
            else if (iSection == 1 && iSchema >= 1 && iType == 0)
            {
                // top-level section
                this.WriteLine(
                    "\r\n" +
                    "<script type=\"text/javascript\">\r\n" +
                    "<!--\r\n" +
                    "    parent.index.location.replace(\"../../toc-1.htm#\");\r\n" +
                    "//-->\r\n" +
                    "</script>\r\n");
            }
            else if (iSchema == 0)
            {
                // top-level section
                this.WriteLine(
                    "\r\n" +
                    "<script type=\"text/javascript\">\r\n" +
                    "<!--\r\n" +
                    "    parent.index.location.replace(\"toc-" + iSection.ToString() + ".htm#\");\r\n" +
                    "//-->\r\n" +
                    "</script>\r\n");
            }
            else if ((iSection != 4 && iType > 0) ||
                (iSection == 1 && iSchema == 1))// || (iSection == 1 && iSchema == 1))
            {
                string linkprefix = "../";
                if (iSection == 4 && iSchema == 1)
                {
                    linkprefix = "";
                }

                this.WriteLine(
                    "\r\n" +
                    "<script type=\"text/javascript\">\r\n" +
                    "<!--\r\n" +
                    "    parent.index.location.replace(\"" + linkprefix + "../toc-" + iSection.ToString() + ".htm#" + iSection.ToString() + "." + iSchema.ToString() + "." + iType.ToString() + "." + iItem.ToString() + "\");\r\n" +
                    "//-->\r\n" +
                    "</script>\r\n");
            }
            else
            {
                string linkprefix = "";
                this.WriteLine(
                    "\r\n" +
                    "<script type=\"text/javascript\">\r\n" +
                    "<!--\r\n" +
                    "    parent.index.location.replace(\"" + linkprefix + "../toc-" + iSection.ToString() + ".htm#" + iSection.ToString() + "." + iSchema.ToString() + "\");\r\n" +
                    "//-->\r\n" +
                    "</script>\r\n");
            }
        }

        internal void WriteContentRefs(List<IfcDoc.DocumentationISO.ContentRef> listFigures, string prefix)
        {
            for (int iFigure = 0; iFigure < listFigures.Count; iFigure++)
            {
                DocObject target = listFigures[iFigure].Page;
                string figurename = listFigures[iFigure].Caption;

                string link = "";
                if (target is DocTemplateDefinition)
                {
                    link = "schema/templates/" + DocumentationISO.MakeLinkName(target) + ".htm";
                }
                else if (target is DocEntity || target is DocType)
                {
                    string schema = this.m_mapSchema[target.Name];
                    link = "schema/" + schema.ToLower() + "/lexical/" + DocumentationISO.MakeLinkName(target) + ".htm";
                }
                else if (target is DocSchema)
                {
                    link = "schema/" + target.Name.ToLower() + "/content.htm";
                }
                else if (target is DocExample)
                {
                    link = "annex/annex-e/" + DocumentationISO.MakeLinkName(target) + ".htm";
                }

                this.m_writer.WriteLine("<a class=\"listing-link\" href=\"" + link + "\">" + prefix + " " + (iFigure + 1).ToString() + " &mdash; " + figurename + "</a><br />");
            }
        }

        public void WriteChangeItem(DocChangeAction docChange, int level)
        {
            // don't output if no change, and no sub-items have changed
            if (!docChange.HasChanges())
                return;

            bool inverse = (docChange.Status == "INVERSE");
            
            StringBuilder sb = new StringBuilder();
            sb.Append("<tr>");


            if (level == 0)
            {
                // section
                sb.Append("<td colspan=\"5\"><b>");
                sb.Append(docChange.Name.ToUpper());
                sb.Append("</b></td>");
            }
            else if (level == 1)
            {
                // schema
                sb.Append("<td>&nbsp;&nbsp;<b>");
                sb.Append(docChange.Name.ToUpper());
                sb.Append("</b></td>");
            }
            else if (level == 2)
            {
                sb.Append("<td>&nbsp;&nbsp;&nbsp;&nbsp;");

                // entity or type                
                string schema = null;
                DocObject docobj = null;
                if (this.m_mapSchema.TryGetValue(docChange.Name, out schema) && this.m_mapEntity.TryGetValue(docChange.Name, out docobj) && (this.m_included == null || this.m_included.ContainsKey(docobj)))
                {
                    if (docChange.Name.StartsWith("Pset_"))
                    {
                        string hyperlink = @"../../../schema/" + schema.ToLower() + @"/pset/" + docChange.Name.ToLower() + ".htm";
                        sb.Append("<a href=\"" + hyperlink + "\">" + docChange.Name + "</a>");
                    }
                    else if (docChange.Name.StartsWith("Qto_"))
                    {
                        string hyperlink = @"../../../schema/" + schema.ToLower() + @"/qset/" + docChange.Name.ToLower() + ".htm";
                        sb.Append("<a href=\"" + hyperlink + "\">" + docChange.Name + "</a>");
                    }
                    else
                    {
                        string hyperlink = @"../../../schema/" + schema.ToLower() + @"/lexical/" + docChange.Name.ToLower() + ".htm";
                        sb.Append("<a href=\"" + hyperlink + "\">" + docChange.Name + "</a>");
                    }
                }
                else if (docChange.Action == DocChangeActionEnum.DELETED)
                {
                    sb.Append(docChange.Name);
                }
                else
                {
                    return;
                }

                sb.Append("</td>");
            }
            else
            {
                sb.Append("<td>");
                for (int i = 0; i < level; i++)
                {
                    sb.Append("&nbsp;&nbsp;");
                }

                if (inverse)
                {
                    sb.Append("<i>");
                }

                sb.Append(docChange.Name);
                if (inverse)
                {
                    sb.Append("</i>");
                }

                sb.Append("</td>");
            }

            if (level > 0)
            {
                // IFC-SPF
                sb.Append("<td>");
                if (docChange.ImpactSPF && !inverse)
                {
                    sb.Append("X");
                }
                sb.Append("</td>");

                // IFC-XML
                sb.Append("<td>");
                if (docChange.ImpactXML)
                {
                    sb.Append("X");
                }
                sb.Append("</td>");

                // change
                sb.Append("<td>");
                if (docChange.Action != DocChangeActionEnum.NOCHANGE)
                {
                    sb.Append(docChange.Action.ToString());
                }
                sb.Append("</td>");

                // description
                sb.Append("<td>");
                foreach (DocChangeAspect docAspect in docChange.Aspects)
                {
                    sb.Append(docAspect.ToString());
                    sb.Append("<br/>");
                }
                sb.Append("</td>");

                sb.Append("</tr>");
            }

            this.WriteLine(sb.ToString());

            // recurse
            foreach (DocChangeAction docSub in docChange.Changes)
            {
                this.WriteChangeItem(docSub, level + 1);
            }
        }

        public static void WriteTOCforTemplates(List<DocTemplateDefinition> list, int level, string prefix, FormatHTM htmTOC, FormatHTM htmSectionTOC, Dictionary<DocObject, bool> included)
        {
            if (list == null)
                return;

            int iTemplateDef = 0;
            foreach (DocTemplateDefinition docTemplateDef in list)
            {
                if (included == null || included.ContainsKey(docTemplateDef))
                {
                    iTemplateDef++;

                    string rellink = DocumentationISO.MakeLinkName(docTemplateDef) + ".htm";
                    htmTOC.WriteTOC(level, "<a href=\"schema/templates/" + rellink + "\">" + prefix + "." + iTemplateDef.ToString() + " " + docTemplateDef.Name + "</a>");

                    if (level == 1)
                    {
                        htmSectionTOC.WriteLine("<tr><td>&nbsp;</td></tr>");
                    }
                    htmSectionTOC.WriteLine("<tr class=\"std\"><td class=\"menu\">" + prefix + "." + iTemplateDef.ToString() + " <a name=\"" + prefix + "." + iTemplateDef.ToString() + "\" /><a class=\"listing-link\" target=\"info\" href=\"templates/" + rellink + "\" >" + docTemplateDef.Name + "</a></td></tr>");

                    // recurse                    
                    WriteTOCforTemplates(docTemplateDef.Templates, level + 1, prefix + "." + iTemplateDef.ToString(), htmTOC, htmSectionTOC, included);
                }
            }
        }

        public void WriteAnchor(DocObject docobj)
        {
            this.WriteLine("<a name=\"" + docobj.Name.ToLower() + "\" />");
        }

        /// <summary>
        /// Writes entire EXPRESS schema as HTML with hyperlinks.
        /// </summary>
        public void WriteExpressSchema(DocProject docProject)
        {
            SortedList<string, DocDefined> mapDefined = new SortedList<string, DocDefined>(this);
            SortedList<string, DocEnumeration> mapEnum = new SortedList<string, DocEnumeration>(this);
            SortedList<string, DocSelect> mapSelect = new SortedList<string, DocSelect>(this);
            SortedList<string, DocEntity> mapEntity = new SortedList<string, DocEntity>(this);
            SortedList<string, DocFunction> mapFunction = new SortedList<string, DocFunction>(this);
            SortedList<string, DocGlobalRule> mapRule = new SortedList<string, DocGlobalRule>(this);

            SortedList<string, DocObject> mapGeneral = new SortedList<string, DocObject>();

            foreach (DocSection docSection in docProject.Sections)
            {
                foreach (DocSchema docSchema in docSection.Schemas)
                {
                    if (this.m_included == null || this.m_included.ContainsKey(docSchema))
                    {
                        foreach (DocType docType in docSchema.Types)
                        {
                            if (this.m_included == null || this.m_included.ContainsKey(docType))
                            {
                                if (docType is DocDefined)
                                {
                                    if (!mapDefined.ContainsKey(docType.Name))
                                    {
                                        mapDefined.Add(docType.Name, (DocDefined)docType);
                                    }
                                }
                                else if (docType is DocEnumeration)
                                {
                                    mapEnum.Add(docType.Name, (DocEnumeration)docType);
                                }
                                else if (docType is DocSelect)
                                {
                                    mapSelect.Add(docType.Name, (DocSelect)docType);
                                }

                                if (!mapGeneral.ContainsKey(docType.Name))
                                {
                                    mapGeneral.Add(docType.Name, docType);
                                }
                            }
                        }

                        foreach (DocEntity docEnt in docSchema.Entities)
                        {
                            if (this.m_included == null || this.m_included.ContainsKey(docEnt))
                            {
                                if (!mapEntity.ContainsKey(docEnt.Name))
                                {
                                    mapEntity.Add(docEnt.Name, docEnt);
                                }
                                if (!mapGeneral.ContainsKey(docEnt.Name))
                                {
                                    mapGeneral.Add(docEnt.Name, docEnt);
                                }
                            }
                        }

                        foreach (DocFunction docFunc in docSchema.Functions)
                        {
                            if ((this.m_included == null || this.m_included.ContainsKey(docFunc)) && !mapFunction.ContainsKey(docFunc.Name))
                            {
                                mapFunction.Add(docFunc.Name, docFunc);
                            }
                        }

                        foreach (DocGlobalRule docRule in docSchema.GlobalRules)
                        {
                            if (this.m_included == null || this.m_included.ContainsKey(docRule))
                            {
                                mapRule.Add(docRule.Name, docRule);
                            }
                        }
                    }
                }
            }

            string schemaid = docProject.Annexes[1].Code; // Computer interpretable listings                

            this.m_writer.WriteLine("<span class=\"express\">");

            this.m_writer.WriteLine("SCHEMA " + schemaid + ";");
            this.m_writer.WriteLine("<br/>");
            this.m_writer.WriteLine("<br/>");

            // if MVD, export IfcStrippedOptional
#if false
            bool mvd = false;
            if (docProject.ModelViews != null)
            {
                foreach (DocModelView docView in docProject.ModelViews)
                {
                    if (docView.Visible && docView.Exchanges.Count > 0)
                    {
                        mvd = true;
                    }
                }
            }

            if (mvd)
            {
                this.m_writer.WriteLine("TYPE IfcStrippedOptional = BOOLEAN;");
                this.m_writer.WriteLine("<br/>");
                this.m_writer.WriteLine("END_TYPE;");
                this.m_writer.WriteLine("<br/>");
                this.m_writer.WriteLine("<br/>");
            }
#endif

            foreach (DocDefined docDef in mapDefined.Values)
            {
                this.WriteAnchor(docDef);
                this.WriteExpressType(docDef);
                this.WriteLine("<br/>");
            }

            foreach (DocEnumeration docEnum in mapEnum.Values)
            {
                this.WriteAnchor(docEnum);
                this.WriteExpressType(docEnum);
                this.WriteLine("<br/>");
            }

            foreach (DocSelect docSelect in mapSelect.Values)
            {
                this.WriteAnchor(docSelect);
                this.WriteExpressType(docSelect);
                this.WriteLine("<br/>");
            }

            foreach (DocEntity docEntity in mapEntity.Values)
            {
                this.WriteAnchor(docEntity);
                this.WriteExpressEntity(docEntity);
                this.WriteLine("<br/>");
            }

            foreach (DocFunction docFunction in mapFunction.Values)
            {
                this.WriteAnchor(docFunction);
                this.WriteExpressFunction(docFunction);
                this.WriteLine("<br/>");
            }

            foreach (DocGlobalRule docRule in mapRule.Values)
            {
                this.WriteAnchor(docRule);
                this.WriteExpressGlobalRule(docRule);
                this.WriteLine("<br/>");
            }

            this.m_writer.WriteLine("END_SCHEMA;");
            this.m_writer.WriteLine("<br/>");

            this.m_writer.WriteLine("</span>");
        }

        #region IComparer Members

        public int Compare(string x, string y)
        {
            return String.CompareOrdinal((string)x, (string)y);
        }

        #endregion


        internal void WriteProperties(List<DocProperty> list)
        {
            if (list.Count == 0)
                return;

            this.WriteLine("<table class=\"gridtable\">");
            this.WriteLine("<tr><th>Name</th><th>Type</th><th>Description</th></tr>");

            foreach (DocProperty docprop in list)
            {
                string datatype = docprop.PrimaryDataType;
                if (datatype == null)
                {
                    datatype = "IfcLabel";
                }

                this.WriteLine("<tr><td>" + docprop.Name + "</td><td>");
                this.WriteDefinition(docprop.PropertyType.ToString());
                this.WriteLine("/");
                this.WriteDefinition(datatype.Trim());
                if (!String.IsNullOrEmpty(docprop.SecondaryDataType))
                {
                    this.WriteLine("/");

                    string[] parts = docprop.SecondaryDataType.Split(':');
                    string enumname = parts[0];
                    DocObject docObj = null;
                    string schema = null;
                    if (m_mapEntity.TryGetValue(enumname, out docObj) && m_mapSchema.TryGetValue(enumname, out schema) && docObj is DocPropertyEnumeration)
                    {
                        this.Write(" <a href=\"../../" + schema.ToLower() + "/pset/" + enumname.ToLower() + ".htm\">" + enumname + "</a>");
                    }
                    else
                    {
                        this.WriteDefinition(docprop.SecondaryDataType.Trim().Replace(",", ", ").Replace(":", ": "));
                    }
                }
                this.WriteLine("</td><td>");

                // english by default
                //this.WriteLine("<tr valign=\"top\"><td><image src=\"../../../img/locale-en.png\" /></td><td><b>" + docprop.Name + "</b>: " + docprop.Documentation + "<br /></td></tr>");

                bool showdefaultdesc = true;
                docprop.Localization.Sort();

                if (docprop.Localization.Count > 0)
                {
                    this.WriteLine("<table class=\"gridtable\">");
                    foreach (DocLocalization doclocal in docprop.Localization)
                    {
                        string localname = doclocal.Name;
                        string localdesc = doclocal.Documentation;
                        string localid = doclocal.Locale.Substring(0, 2).ToLower();

                        if (String.IsNullOrEmpty(doclocal.Documentation) && localid.Equals("en", StringComparison.InvariantCultureIgnoreCase) && localdesc == null)
                        {
                            localdesc = docprop.Documentation;
                            showdefaultdesc = false;
                        }

                        this.WriteLine("<tr><td><img src=\"../../../img/locale-" + localid + ".png\" /></td><td><b>" + localname + "</b></td><td>" + localdesc + "</td></tr>");
                    }
                    this.WriteLine("</table>");
                }

                if(showdefaultdesc)
                {
                    this.WriteLine(docprop.Documentation); // locale-generic
                }

                // complex properties
                if (docprop.Elements != null)
                {
                    WriteProperties(docprop.Elements);
                }

                this.WriteLine("</td></tr>");
            }

            this.WriteLine("</table>");
           
        }

        internal void WriteLinkTo(DocObject type)
        {
            int levels = 3;
            if (type is DocExample || type is DocTemplateDefinition || type is DocSchema)
            {
                levels = 2;
            }

            this.WriteLinkTo(DocumentationISO.MakeLinkName(type), levels);
        }

        internal void WriteLinkTo(string identifier, int levels)
        {
            string up = "";
            for (int i = 0; i < levels; i++ )
            {
                up += "../";
            }

            this.WriteLine("<p><a href=\"" + up + "link/" + identifier + ".htm\" target=\"_top\" ><img src=\"" + up + "img/permlink.png\" style=\"border: 0px\" title=\"Link to this page\" alt=\"Link to this page\"/>&nbsp; Link to this page</a></p>");
        }

        internal void WriteLinkPage(string linkurl)
        {
            this.WriteLine(
            "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01 Frameset//EN\"" +
               "\"http://www.w3.org/TR/html4/frameset.dtd\">" +
            "<html lang=\"en\">" +
            "<head>" +
            "	<meta content=\"text/html; charset=utf-8\" http-equiv=\"content-type\">" +
            "	<link rel=\"STYLESHEET\" href=\"../ifc-styles.css\">" +
            "	<title>" + Properties.Settings.Default.Header + "</title>" +
            "	<style type=\"text/css\">" +
            "	<!--" +
            "	frameset {" +
            "	  border-width: 1px;" +
            "	  border-color: rgb(255,255,255);" +
            "	  border-style: solid;" +
            "	}" +
            "	-->" +
            "   </style>" +
            "</head>" +

            "<frameset rows=\"110px,*\">" +
            "    <frame src=\"../content.htm\" name=\"menu\" frameborder=\"0\">" +
            "	<frameset cols=\"15%,*\">" +
            "		<frame src=\"../blank.htm\" name=\"index\" frameborder=\"0\">" +
            "		<frame src=\"" + linkurl + "\" name=\"info\" frameborder=\"0\">" +
            "	</frameset>" +
            "	<noframes>" +
            "		<body>" +
            "			<p>Industry Foundation Classes (IFC) for Data Sharing in the Construction and Facility Management Industries </p>" +
            "			<p>&nbsp;</p>" +
            "		</body>" +
            "	</noframes>" +
            "</frameset>" +

            "</html>");

        }

        internal void WriteComputerListing(string name, string code, int iCodeView)
        {
            int iAnnex = -1;

            string codexml = code;
            if (codexml == "ifc4")
            {
                codexml = "ifcXML4";
            }

            string linkprefix = code;
            string linkprefixxml = codexml;
            if (iCodeView == 0)
            {
                linkprefix = "annex-a/default/" + linkprefix;
                linkprefixxml = "annex-a/default/" + linkprefixxml;
            }

            if (iCodeView > 0)
            {
                this.WriteHeader(name, iAnnex, iCodeView, 0, 0, Properties.Settings.Default.Header);
                this.WriteScript(iAnnex, iCodeView, 0, 0);
                this.WriteLine("<h3 class=\"std\">A." + iCodeView.ToString() + " " + name + "</h3>");
            }


            string key1 = "";// "A." + iCodeView + ".1";
            string key2 = "";// "A." + iCodeView + ".2";
            string key3 = "";// "A." + iCodeView + ".3";

            if (iCodeView > 0)//== 0 || Properties.Settings.Default.ConceptTables)
            {
                // write table linking formatted listings
                this.Write(
                    "<h4 class=\"annex\"><a>" + key1 + " Schema definitions</a></h4>" +
                    "<p class=\"std\">This schema is defined within EXPRESS and XSD files.</p>" +
                    "<p class=\"std\">&nbsp;</p>" +
                    "<table class=\"std centric\" summary=\"listings\" width=\"80%\">" +
                    "<col width=\"60%\">" +
                    "<col width=\"20%\">" +
                    "<col width=\"20%\">" +
                    "<tr style=\"border: 1px grey solid;\">" +
                    "<th>Description</td>" +
                    "<th>ASCII file</td>" +
                    "<th>HTML file</td>" +
                    "</tr>" +
                    "<tr>" +
                    "<td>IFC EXPRESS long form schema</td>" +
                    "<td><a href=\"" + linkprefix + ".exp\">" + code + ".exp</a></td>" +
                    "<td><a href=\"" + linkprefix + ".exp.htm\">" + code + ".exp.htm</a></td>" +
                    "</tr>" +
                    "<tr>" +
                    "<td>IFC XSD long form schema</td>" +
                    "<td><a href=\"" + linkprefixxml + ".xsd\">" + codexml + ".xsd</a></td>" +
                    "<td><a href=\"" + linkprefixxml + ".xsd.htm\">" + codexml + ".xsd.htm</a></td>" +
                    "</tr>" +
                    "</table>");

                this.Write(
                    "<h4 class=\"annex\"><a>" + key2 + " Property and quantity templates</a></h4>" +
                    "<p>Property sets and quantity sets are defined within IFC-SPF and IFC-XML files.</p>" +
                    "<p>&nbsp;</p>" +
                    "<table class=\"std centric\" summary=\"listings\" width=\"80%\">" +
                    "<col width=\"60%\">" +
                    "<col width=\"20%\">" +
                    "<col width=\"20%\">" +
                    "<tr style=\"border: 1px grey solid;\">" +
                    "<th>Description</td>" +
                    "<th>ASCII file</td>" +
                    "<th>HTML file</td>" +
                    "</tr>" +
                    "<tr>" +
                    "<td>IFC-SPF property and quantity templates</td>" +
                    "<td><a href=\"" + linkprefix + ".ifc\">" + code + ".ifc</a></td>" +
                    "<td>&nbsp;</td>" +
                    "</tr>" +
                    "<tr>" +
                    "<td>IFC-XML property and quantity templates</td>" +
                    "<td><a href=\"" + linkprefix + ".ifcxml\">" + code + ".ifcxml</a></td>" +
                    "<td>&nbsp;</td>" +
                    "</tr>" +

                    "</table>");
            }

            if (iCodeView > 0)
            {
                this.Write(
    "<h4 class=\"annex\"><a>" + key3 + " Model view definition</a></h4>" +
    "<p>Model view definitions are defined within MVD-XML files.</p>" +
    "<p>&nbsp;</p>" +
    "<table class=\"std centric\" summary=\"listings\" width=\"80%\">" +
    "<col width=\"60%\">" +
    "<col width=\"20%\">" +
    "<col width=\"20%\">" +
    "<tr style=\"border: 1px grey solid;\">" +
    "<th>Description</td>" +
    "<th>ASCII file</td>" +
    "<th>HTML file</td>" +
    "</tr>" +
    "<tr>" +
    "<td>MVD-XML model view definitions</td>" +
    "<td><a href=\"" + linkprefix + ".mvdxml\">" + code + ".mvdxml</a></td>" +
    "<td>&nbsp;</td>" +
    "</tr>" +
    "<tr>" +
    "<td>EXPRESS XSD configuration</td>" +
    "<td><a href=\"" + linkprefix + ".xml\">" + code + ".xml</a></td>" +
    "<td>&nbsp;</td>" +
    "</tr>" +
    "</table>");
            }

            this.WriteLinkTo("listing-" + code.ToLower(), 3);
            this.WriteFooter(Properties.Settings.Default.Footer);

        }

        /// <summary>
        /// Writes page for index into EXPRESS-G diagrams
        /// </summary>
        /// <param name="docSection"></param>
        /// <param name="iSection">Section number (1-based)</param>
        internal void WriteDiagramListing(DocSection docSection, int iSection)
        {
            int iAnnex = -4;
            int iSub = 1;

            this.WriteHeader(docSection.Name, 2);
            this.WriteScript(iAnnex, iSub, iSection, 0);
            this.WriteLine("<h3 class=\"std\">D.1." + iSection.ToString() + " " + docSection.Name + "</h3>");

            int iSchema = 0;
            foreach (DocSchema docSchema in docSection.Schemas)
            {
                iSchema++;
                this.WriteLine("<h4 class=\"std\">D.1." + iSection + "." + iSchema + " " + docSchema.Name + "</h4>");

                this.WriteLine("<p>");
                
                // determine number of diagrams
                int iLastDiagram = docSchema.GetDiagramCount();

                // write thumbnail links for each diagram
                for (int iDiagram = 1; iDiagram <= iLastDiagram; iDiagram++)
                {
                    string formatnumber = iDiagram.ToString("D4"); // 0001
                    this.WriteLine("<a href=\"" + docSchema.Name.ToLower() + "/diagram_" + formatnumber + ".htm\">" +
                        "<img src=\"" + docSchema.Name.ToLower() + "/small_diagram_" + formatnumber + ".png\" width=\"100\" height=\"148\" /></a>"); // width=\"150\" height=\"222\"> 
                }

                this.WriteLine("</p>");
            }

            this.WriteFooter(Properties.Settings.Default.Footer);
        }

        public void WriteLocalizedNames(DocDefinition entity)
        {
            // localization
            this.WriteLine("<details>");
            this.WriteLine("<summary>Natural language names</summary>");
            this.WriteLine("<table>");
            entity.Localization.Sort();
            foreach (DocLocalization doclocal in entity.Localization)
            {
                string localname = doclocal.Name;
                string localdesc = doclocal.Documentation;
                string localid = doclocal.Locale.Substring(0, 2).ToLower();

                if (!String.IsNullOrEmpty(localdesc))
                {
                    localdesc = ": " + localdesc;
                }

                this.WriteLine("<tr><td><img alt=\"" + localid + "\" src=\"../../../img/locale-" + localid + ".png\" /></td><td><b>" + localname + "</b>" + localdesc + "</td></tr>");
            }
            this.WriteLine("</table>");
            this.WriteLine("</details>");
        }

        public void WriteEntityInheritance(DocEntity entity, DocEntity treeleaf, DocModelView[] views, Dictionary<DocObject, bool>[] viewmap, ref int sequence)
        {
            if (entity.BaseDefinition != null)
            {
                if (this.m_mapEntity.ContainsKey(entity.BaseDefinition))
                {
                    DocEntity baseEntity = (DocEntity)this.m_mapEntity[entity.BaseDefinition];
                    WriteEntityInheritance(baseEntity, treeleaf, views, viewmap, ref sequence);
                }
            }

            int colspan = 5;

            if (views != null)
            {
                colspan += views.Length;
            }

            this.Write("<tr><td colspan=\"" + colspan + "\">");
            if(entity.IsAbstract())
            {
                this.Write("<i>");
            }
            this.Write(FormatDefinition(entity.Name));
            if (entity.IsAbstract())
            {
                this.Write("</i>");
            }
            this.WriteLine("</td></tr>");

            WriteEntityAttributes(entity, treeleaf, views, viewmap, ref sequence);
        }

        private void WriteEntityAttributeViews(DocAttribute docAttr, DocModelView[] views, Dictionary<DocObject, bool>[] viewmap)
        {
            if (views == null || viewmap == null)
                return;

            for (int iView = 0; iView < viewmap.Length; iView++)
            {
                if (viewmap[iView] != null)
                {
                    this.m_writer.Write("<td>");
                    bool included = false;
                    if (viewmap[iView].TryGetValue(docAttr, out included))
                    {
                        this.m_writer.Write("X");
                    }
                    this.m_writer.Write("</td>");
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="treeleaf"></param>
        /// <param name="sequence">Last sequence number used (0 initially)</param>
        public void WriteEntityAttributes(DocEntity entity, DocEntity treeleaf, DocModelView[] views, Dictionary<DocObject, bool>[] viewmap, ref int sequence)
        {
            bool bInverse = false;
            bool bDerived = false;
            bool bExplicit = false;

            bool suppresshistory = true;

            // count attributes first to avoid generating tables unnecessarily (W3C validation)
            if (entity.Attributes != null && entity.Attributes.Count > 0)
            {
                foreach (DocAttribute attr in entity.Attributes)
                {
                    if (attr.Derived != null)
                    {
                        // inverse may also be indicated to hold class
                        bDerived = true;
                    }
                    else if (attr.Inverse != null)
                    {                        
                        bInverse = true;
                    }
                    else
                    {
                        bExplicit = true;
                    }
                }
            }            

            // explicit attributes, plus detect any inverse or derived
            if (bExplicit)
            {
                foreach (DocAttribute attr in entity.Attributes)
                {
                    bool bInclude = true;

                    // suppress any attribute that is overridden on leaf class
                    if (treeleaf != null && treeleaf != entity)
                    {
                        foreach (DocAttribute derivedattr in treeleaf.Attributes)
                        {
                            if (derivedattr.Name.Equals(attr.Name))
                            {
                                bInclude = false;
                                sequence++;
                            }
                        }
                    }

                    if (bInclude)
                    {
                        if (attr.Inverse == null && attr.Derived == null)
                        {
                            sequence++;

                            this.m_writer.Write("<tr><td>");
                            this.m_writer.Write(sequence.ToString());
                            this.m_writer.Write("</td><td>");
                            this.m_writer.Write(attr.Name);
                            this.m_writer.Write("</td><td>");

                            if (this.m_included == null || this.m_included.ContainsKey(attr))
                            {
                                this.m_writer.Write(FormatDefinition(attr.DefinedType));
                            }
                            else
                            {
                                this.m_writer.Write("<span class=\"self-ref\">-</span>");
                            }

                            this.m_writer.Write("</td><td>");

                            if (this.m_included == null || this.m_included.ContainsKey(attr))
                            {
                                if(attr.GetAggregation() != DocAggregationEnum.NONE)
                                {
                                    this.WriteAttributeAggregation(attr);
                                }
                                else if ((attr.AttributeFlags & 1) != 0)
                                {
                                    this.m_writer.Write("[0:1]");
                                }
                                else
                                {
                                    this.m_writer.Write("[1:1]");
                                }
                            }

                            this.m_writer.WriteLine("</td><td>");
                            if (this.m_included == null || this.m_included.ContainsKey(attr))
                            {
                                this.WriteDocumentationForISO(attr.Documentation, entity, suppresshistory);
                            }
                            else
                            {
                                this.m_writer.Write("<i>This attribute is out of scope for this model view definition and shall not be set.</i>");
                            }
                            this.m_writer.Write("</td>");
                            this.WriteEntityAttributeViews(attr, views, viewmap);                            
                            this.m_writer.WriteLine("</tr>");

                        }
                    }
                }
            }

            // inverse attributes
            if (bInverse)
            {
                foreach (DocAttribute attr in entity.Attributes)
                {
                    DocObject docinvtype = null;
                    if (attr.Inverse != null && attr.Derived == null && this.m_mapEntity.TryGetValue(attr.DefinedType, out docinvtype))
                    {
                        if (this.m_included == null || this.m_included.ContainsKey(docinvtype))
                        {
                            this.m_writer.Write("<tr><td></td><td><i>");
                            this.m_writer.Write(attr.Name);
                            this.m_writer.Write("</i>");
                            this.m_writer.Write("</td><td>");

                            this.m_writer.Write(FormatDefinition(attr.DefinedType));
                            this.m_writer.Write("<br/>@" + attr.Inverse);

                            this.m_writer.Write("</td><td>");

                            if (attr.GetAggregation() != DocAggregationEnum.NONE)
                            {
                                this.WriteAttributeAggregation(attr);
                            }
                            else if ((attr.AttributeFlags & 1) != 0)
                            {
                                this.m_writer.Write("[0:1]");
                            }
                            else
                            {
                                this.m_writer.Write("[1:1]");
                            }


                            this.m_writer.Write("</td><td>");
                            this.WriteDocumentationForISO(attr.Documentation, entity, suppresshistory);
                            this.m_writer.Write("</td>");

                            this.WriteEntityAttributeViews(attr, views, viewmap);

                            this.m_writer.WriteLine("</tr>");
                        }
                    }
                }
            }

            // derived attributes
            if (bDerived)
            {
                foreach (DocAttribute attr in entity.Attributes)
                {
                    if (attr.Derived != null)
                    {
                        // determine the superclass having the attribute                        
                        DocEntity found = null;
                        if (treeleaf == null)
                        {
                            DocEntity super = entity;
                            while (super != null && found == null && super.BaseDefinition != null)
                            {
                                super = this.m_mapEntity[super.BaseDefinition] as DocEntity;
                                if (super != null)
                                {
                                    foreach (DocAttribute docattr in super.Attributes)
                                    {
                                        if (docattr.Name.Equals(attr.Name))
                                        {
                                            // found class
                                            found = super;
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        if (found != null)
                        {
                            // overridden attribute
                            this.m_writer.Write("<tr><td></td><td>" + "\\" + found.Name + "." + attr.Name);
                        }
                        else
                        {
                            // non-overridden
                            this.m_writer.Write("<tr><td></td><td>" + attr.Name);
                        }
                        this.m_writer.Write("<br/>:=" + attr.Derived);

                        this.m_writer.Write("</td><td>");
                        this.m_writer.Write(FormatDefinition(attr.DefinedType));
                        this.m_writer.Write("</td><td>");
                        //this.m_writer.Write(" := ");

                        if (attr.GetAggregation() != DocAggregationEnum.NONE)
                        {
                            this.WriteAttributeAggregation(attr);
                        }
                        else if ((attr.AttributeFlags & 1) != 0)
                        {
                            this.m_writer.Write("[0:1]");
                        }
                        else
                        {
                            this.m_writer.Write("[1:1]");
                        }

                        this.m_writer.Write("</td><td>");
                        this.WriteDocumentationForISO(attr.Documentation, entity, suppresshistory);
                        this.m_writer.WriteLine("</td>");
                        
                        this.WriteEntityAttributeViews(attr, views, viewmap);

                        this.m_writer.WriteLine("</tr>");

                    }
                }
            }


        }

        internal void WriteTerm(DocTerm docRef)
        {
            this.WriteLine("<dt class=\"term\"><strong name=\"" + DocumentationISO.MakeLinkName(docRef) + "\" id=\"" + DocumentationISO.MakeLinkName(docRef) + "\">" + docRef.Name + "</strong></dt>");
            this.WriteLine("<dd class=\"term\">" + docRef.Documentation);

            if (docRef.Terms.Count > 0)
            {
                this.WriteLine("<dl>");
                foreach (DocTerm sub in docRef.Terms)
                {
                    WriteTerm(sub);
                }
                this.WriteLine("</dl>");
            }

            this.WriteLine("</dd>");
        }

        internal void WriteChangeLog(DocDefinition entity, DocProject docProject)
        {
            Dictionary<DocChangeSet, DocChangeAction> mapChange = new Dictionary<DocChangeSet, DocChangeAction>();
            foreach (DocChangeSet docChangeSet in docProject.ChangeSets)
            {
                foreach (DocChangeAction docChangeSection in docChangeSet.ChangesEntities)
                {
                    foreach (DocChangeAction docChangeSchema in docChangeSection.Changes)
                    {
                        foreach (DocChangeAction docChangeEntity in docChangeSchema.Changes)
                        {
                            if (docChangeEntity.Name.Equals(entity.Name) && docChangeEntity.HasChanges())
                            {
                                try
                                {
                                    // could show up twice, for case of an entity moving between schemas
                                    if (!mapChange.ContainsKey(docChangeSet))
                                    {
                                        mapChange.Add(docChangeSet, docChangeEntity);
                                    }
                                    else
                                    {
                                        // overwrite with new schema
                                        DocChangeAction docPrev = mapChange[docChangeSet];
                                        if(docChangeEntity.Changes.Count > docPrev.Changes.Count)
                                        {
                                            mapChange[docChangeSet] = docChangeEntity; 
                                        }
                                    }
                                }
                                catch
                                {
                                    mapChange.ToString();
                                }
                            }
                        }
                    }
                }
            }

            if (mapChange.Count > 0)
            {
                this.WriteLine("<details>");
                this.WriteLine("<summary>Change log</summary>");

                this.WriteLine("<table class=\"gridtable\">");
                this.WriteLine("<tr>" +
                    "<th>Item</th>" +
                    "<th>SPF</th>" +
                    "<th>XML</th>" +
                    "<th>Change</th>" +
                    "<th>Description</th>" +
                    "</tr>");

                foreach (DocChangeSet docChangeSet in mapChange.Keys)
                {
                    this.WriteLine("<td colspan=5><b><a href=\"../../../annex/annex-f/" + DocumentationISO.MakeLinkName(docChangeSet) + "/index.htm\">" + docChangeSet.Name + "</a></b></td>");
                    DocChangeAction docChangeAction = mapChange[docChangeSet];
                    this.WriteChangeItem(docChangeAction, 2);
                }

                this.WriteLine("</table>");

                this.WriteLine("</details>");
            }


        }
    }    

}
