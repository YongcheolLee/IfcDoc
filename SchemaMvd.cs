﻿// Name:        SchemaMvd.cs
// Description: MVD-XML schema
// Author:      Tim Chipman
// Origination: Work performed for BuildingSmart by Constructivity.com LLC.
// Copyright:   (c) 2010 BuildingSmart International Ltd.
// License:     http://www.buildingsmart-tech.org/legal

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Xml.Serialization;

namespace IfcDoc.Schema.MVD
{
    public static class SchemaMVD
    {
        static Dictionary<string, Type> s_types;

        public static Dictionary<string, Type> Types
        {
            get
            {
                if (s_types == null)
                {
                    s_types = new Dictionary<string, Type>();

                    Type[] types = typeof(SchemaMVD).Assembly.GetTypes();
                    foreach (Type t in types)
                    {
                        if (typeof(SEntity).IsAssignableFrom(t) && !t.IsAbstract && t.Namespace.Equals("IfcDoc.Schema.MVD"))
                        {
                            string name = t.Name.ToUpper();
                            s_types.Add(name, t);
                        }
                    }
                }

                return s_types;
            }
        }
    }
    
    public abstract class Identity : SEntity
    {
        [DataMember(Order = 0), XmlAttribute("uuid")] public Guid Uuid;
        [DataMember(Order = 1), XmlAttribute("name")] public string Name;
        [DataMember(Order = 2), XmlAttribute("code")] public string Code; // e.g. 'bsi-100'
        [DataMember(Order = 3), XmlAttribute("version")] public string Version;
        [DataMember(Order = 4), XmlAttribute("status")] public string Status; // e.g. 'draft'
        [DataMember(Order = 5), XmlAttribute("author")] public string Author;
        [DataMember(Order = 6), XmlAttribute("owner")] public string Owner; // e.g. 'buildingSMART international'
        [DataMember(Order = 7), XmlAttribute("copyright")] public string Copyright;
    }

    public abstract class Element : Identity
    {
        [DataMember(Order = 0)] public List<Definition> Definitions;
    }

    [XmlType("mvdXML")]
    public class mvdXML : Identity
    {
        [DataMember(Order = 0)] public List<ConceptTemplate> Templates = new List<ConceptTemplate>();
        [DataMember(Order = 1)] public List<ModelView> Views = new List<ModelView>();

        // namespaces in order of attempts to load
        public static readonly string[] Namespaces = new string[]
        {
            DefaultNamespace,
            "http://buildingsmart-tech.org/mvdXML/mvdXML1-0",
            "http://buildingsmart-tech.org/mvdXML/mvdXML_V1-0"
        };

        public const string DefaultNamespace = "http://buildingsmart-tech.org/mvdXML/mvdXML1-1";
    }

    [XmlType("ConceptTemplate")]
    public class ConceptTemplate : Element
    {
        [DataMember(Order = 0), XmlAttribute("applicableSchema")] public string ApplicableSchema = "IFC4";
        [DataMember(Order = 1), XmlAttribute("applicableEntity")] public string ApplicableEntity; // was ApplicableEntities before final
        [DataMember(Order = 2)] public List<AttributeRule> Rules; // new in 2.5
        [DataMember(Order = 3)] public List<ConceptTemplate> SubTemplates;
    }

    // used to map xpath
    [XmlType("Template")]
    public class TemplateRef : SEntity
    {
        [DataMember(Order = 0), XmlAttribute("ref")] public Guid Ref;
    }

    [XmlType("BaseConcept")]
    public class BaseConcept : SEntity
    {
        [DataMember(Order = 0), XmlAttribute("ref")] public Guid Ref;
    }

    [XmlType("Concept")]
    public class Concept : Element
    {
        [DataMember(Order = 0)] public TemplateRef Template; // links to ConceptTemplate
        [DataMember(Order = 1)] public List<ConceptRequirement> Requirements = new List<ConceptRequirement>();
        //[DataMember(Order = 2)] public List<TemplateRule> Rules = new List<TemplateRule>();
        [DataMember(Order = 2)] public TemplateRules TemplateRules = new TemplateRules();
        [DataMember(Order = 3)] public List<Concept> SubConcepts; // added v3.8
        [DataMember(Order = 4), XmlAttribute("override")] public bool Override; // added in v5.6
        [DataMember(Order = 5), XmlElement("baseConcept")] public BaseConcept BaseConcept;
    }

    // used to map xpath
    [XmlType("Concept")]
    public class ConceptRef : SEntity
    {
        [DataMember(Order = 0), XmlAttribute("ref")]
        public Guid Ref;
    }

    [XmlType("Requirement")]
    public class ConceptRequirement : SEntity
    {
        [DataMember(Order = 0), XmlAttribute("applicability")] public ApplicabilityEnum Applicability;
        [DataMember(Order = 1), XmlAttribute("requirement")] public RequirementEnum Requirement;
        [DataMember(Order = 2), XmlAttribute("exchangeRequirement")] public Guid ExchangeRequirement; // Uuid
    }

    public enum ApplicabilityEnum
    {
        [XmlEnum("both")] Both = 0,
        [XmlEnum("export")] Export = 1,
        [XmlEnum("import")] Import = 2,
    }

    public enum RequirementEnum
    {
        [XmlEnum("mandatory")] Mandatory = 1,
        [XmlEnum("optional")] Optional = 2,
        [XmlEnum("not relevant")] NotRelevant = 3,
        [XmlEnum("excluded")] Excluded = 4,
    }

    [XmlType("ModelView")]
    public class ModelView : Element
    {
        [DataMember(Order = 0), XmlAttribute("applicableSchema")] public string ApplicableSchema;
        [DataMember(Order = 1)] public List<ExchangeRequirement> ExchangeRequirements = new List<ExchangeRequirement>();
        [DataMember(Order = 2)] public List<ConceptRoot> Roots = new List<ConceptRoot>();
        [DataMember(Order = 3)] public String BaseView;
    }

    [XmlType("Definition")]
    public class Definition : SEntity
    {
        [DataMember(Order = 0)] public Body Body = new Body();
        [DataMember(Order = 1), XmlElement("Link")] public List<Link> Links;
    }

    public class Body : SEntity,
        IXmlSerializable
    {
        [DataMember(Order = 0), XmlText()] public string Content;
        [DataMember(Order = 1), XmlAttribute("lang")] public string Lang;
        [DataMember(Order = 2), XmlAttribute("tags")] public string Tags;

        #region IXmlSerializable Members

        public System.Xml.Schema.XmlSchema GetSchema()
        {
            return null;
        }

        public void ReadXml(System.Xml.XmlReader reader)
        {
            reader.ReadStartElement();
            this.Content = reader.ReadString();
            reader.ReadEndElement();
        }

        public void WriteXml(System.Xml.XmlWriter writer)
        {
            writer.WriteCData(this.Content);
        }

        #endregion
    }

    [XmlType("Link")]
    public class Link : SEntity
    {
        [DataMember(Order = 0), XmlAttribute("lang")] public string Lang;
        [DataMember(Order = 1), XmlAttribute("category")] public CategoryEnum Category;
        [DataMember(Order = 2), XmlAttribute("title")] public string Title;
        [DataMember(Order = 3), XmlAttribute("href")] public string Href;
        [DataMember(Order = 4), XmlText()] public string Content;
    }

    public enum CategoryEnum
    {
        [XmlEnum("definition")] Definition = 0,
        [XmlEnum("agreement")] Agreement = 1,
        [XmlEnum("diagram")] Diagram = 2,
        [XmlEnum("instantiation")] Instantiation = 3,
        [XmlEnum("example")] Example = 4,
    }

    [XmlType("ExchangeRequirement")]
    public class ExchangeRequirement : Element
    {
        [DataMember(Order = 0), XmlAttribute("applicability")] public ApplicabilityEnum Applicability;
    }

    [XmlType("ConceptRoot")]
    public class ConceptRoot : Element
    {
        [DataMember(Order = 0)] public ApplicabilityRules Applicability;
        [DataMember(Order = 1), XmlAttribute("applicableRootEntity")] public string ApplicableRootEntity; // e.g. 'IfcBeam'
        [DataMember(Order = 2)] public List<Concept> Concepts = new List<Concept>(); // really Concept but fixed according to sample data to get xml serializer working
    }

    [XmlType("ApplicabilityRules")]
    public class ApplicabilityRules : Element
    {
        [DataMember(Order = 0)] public TemplateRef Template;
        [DataMember(Order = 1)] public TemplateRules TemplateRules;
    }

    [XmlType("AbstractRule")]
    public abstract class AbstractRule : SEntity
    {
        [DataMember(Order = 0), XmlAttribute("RuleID")] public string RuleID;
        [DataMember(Order = 1), XmlAttribute("Description")] public string Description;
    }

    [XmlType("AttributeRule")]
    public class AttributeRule : AbstractRule
    {
        [DataMember(Order = 0), XmlAttribute("AttributeName")] public string AttributeName;
        //[DataMember(Order = 1), XmlAttribute("Cardinality")] public CardinalityType Cardinality;
        [DataMember(Order = 1)] public List<EntityRule> EntityRules;
        [DataMember(Order = 2)] public List<Constraint> Constraints;
    }

    public enum CardinalityType
    {
        _asSchema = 0, // 3.8: was previously "Default" 
        Zero = 1,
        ZeroToOne = 2,
        One = 3,
        OneToMany = 4
    }

    [XmlType("EntityRule")]
    public class EntityRule : AbstractRule
    {
        [DataMember(Order = 0), XmlAttribute("EntityName")] public string EntityName;
        //[DataMember(Order = 1), XmlAttribute("Cardinality")] public CardinalityType Cardinality;
        [DataMember(Order = 1)] public List<AttributeRule> AttributeRules;
        [DataMember(Order = 2)] public List<Constraint> Constraints;
        [DataMember(Order = 3)] public List<TemplateRef> References; // MVDXML 1.1 -- links to concept templates defined on referenced entity
    }

    [XmlType("Constraint")]
    public class Constraint : SEntity
    {
        [DataMember(Order = 0), XmlAttribute("Expression")] public string Expression;
    }

    [XmlType("TemplateRule")]
    public class TemplateRule : AbstractRule
    {
        [DataMember(Order = 0), XmlAttribute("Parameters")] public string Parameters;
        [DataMember(Order = 1)] public List<Concept> References; // proposed for mvdxml 1.1 -- not yet approved
    }

    [XmlType("TemplateRules")] // added in mvdXML 1.1d
    public class TemplateRules
    {
        [DataMember(Order = 0), XmlAttribute("operator")] public TemplateOperator Operator;
        [DataMember(Order = 1), XmlElement(typeof(TemplateRule))] public List<TemplateRule> TemplateRule = new List<TemplateRule>();
        [DataMember(Order = 2), XmlElement(typeof(TemplateRules))] public TemplateRules InnerRules;
    }

    public enum TemplateOperator // added in mvdXML 1.1d
    {
        [XmlEnum("and")]  And = 0,
        [XmlEnum("or")]   Or = 1,
        [XmlEnum("not")]  Not = 2,
        [XmlEnum("nand")] Nand = 3,
        [XmlEnum("nor")]  Nor = 4,
        [XmlEnum("xor")]  Xor = 5,
        [XmlEnum("nxor")] Nxor = 6,
    }
}
