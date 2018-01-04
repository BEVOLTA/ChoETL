﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace ChoETL
{
    [DataContract]
    public class ChoCSVRecordConfiguration : ChoFileRecordConfiguration
    {
        [DataMember]
        public ChoCSVFileHeaderConfiguration FileHeaderConfiguration
        {
            get;
            set;
        }
        [DataMember]
        public string Delimiter
        {
            get;
            set;
        }
        [DataMember]
        public bool? HasExcelSeparator
        {
            get;
            set;
        }
        [DataMember]
        public List<ChoCSVRecordFieldConfiguration> CSVRecordFieldConfigurations
        {
            get;
            private set;
        }
        [DataMember]
        public bool Sanitize
        {
            get;
            set;
        }
        [DataMember]
        public string InjectionChars
        {
            get;
            set;
        }
        [DataMember]
        public char InjectionEscapeChar
        {
            get;
            set;
        }

        internal int MaxFieldPosition
        {
            get;
            private set;
        }
        internal Dictionary<string, ChoCSVRecordFieldConfiguration> RecordFieldConfigurationsDict
        {
            get;
            private set;
        }
        internal Dictionary<string, ChoCSVRecordFieldConfiguration> RecordFieldConfigurationsDict2
        {
            get;
            private set;
        }

        public ChoCSVRecordConfiguration Configure(Action<ChoCSVRecordConfiguration> action)
        {
            if (action != null)
                action(this);

            return this;
        }

        internal Dictionary<string, string> AlternativeKeys
        {
            get;
            set;
        }

        internal KeyValuePair<string, ChoCSVRecordFieldConfiguration>[] FCArray;

        public ChoCSVRecordFieldConfiguration this[string name]
        {
            get
            {
                return CSVRecordFieldConfigurations.Where(i => i.Name == name).FirstOrDefault();
            }
        }

        public ChoCSVRecordConfiguration() : this(null)
        {

        }

        internal ChoCSVRecordConfiguration(Type recordType) : base(recordType)
        {
            CSVRecordFieldConfigurations = new List<ChoCSVRecordFieldConfiguration>();

            if (recordType != null)
            {
                Init(recordType);
            }

            if (Delimiter.IsNullOrEmpty())
            {
                if (Culture != null)
                    Delimiter = Culture.TextInfo.ListSeparator;

                if (Delimiter.IsNullOrWhiteSpace())
                    Delimiter = ",";
            }

            Sanitize = false;
            InjectionChars = "=@+-";
            InjectionEscapeChar = '\t';

            FileHeaderConfiguration = new ChoCSVFileHeaderConfiguration(recordType, Culture);
        }

        protected override void Init(Type recordType)
        {
            base.Init(recordType);

            ChoCSVRecordObjectAttribute recObjAttr = ChoType.GetAttribute<ChoCSVRecordObjectAttribute>(recordType);
            if (recObjAttr != null)
            {
                Delimiter = recObjAttr.Delimiter;
                HasExcelSeparator = recObjAttr.HasExcelSeparatorInternal;
            }

            DiscoverRecordFields(recordType);
        }

        public override void MapRecordFields<T>()
        {
            DiscoverRecordFields(typeof(T));
        }

        public override void MapRecordFields(Type recordType)
        {
            DiscoverRecordFields(recordType);
        }

        internal bool AreAllFieldTypesNull
        {
            get;
            set;
        }

        internal void UpdateFieldTypesIfAny(IDictionary<string, Type> dict)
        {
            if (dict == null)
                return;

            foreach (var key in dict.Keys)
            {
                if (RecordFieldConfigurationsDict.ContainsKey(key) && dict[key] != null)
                    RecordFieldConfigurationsDict[key].FieldType = dict[key];
            }

            AreAllFieldTypesNull = RecordFieldConfigurationsDict.All(kvp => kvp.Value.FieldType == null);
        }

        private void DiscoverRecordFields(Type recordType)
        {
            if (!IsDynamicObject) //recordType != typeof(ExpandoObject))
            {
                CSVRecordFieldConfigurations.Clear();

                if (ChoTypeDescriptor.GetProperties(recordType).Where(pd => pd.Attributes.OfType<ChoCSVRecordFieldAttribute>().Any()).Any())
                {
                    foreach (PropertyDescriptor pd in ChoTypeDescriptor.GetProperties(recordType).Where(pd => pd.Attributes.OfType<ChoCSVRecordFieldAttribute>().Any()))
                    {
                        //if (!pd.PropertyType.IsSimple())
                        //    throw new ChoRecordConfigurationException("Property '{0}' is not a simple type.".FormatString(pd.Name));

                        var obj = new ChoCSVRecordFieldConfiguration(pd.Name, pd.Attributes.OfType<ChoCSVRecordFieldAttribute>().First());
                        obj.FieldType = pd.PropertyType;
                        CSVRecordFieldConfigurations.Add(obj);
                    }
                }
                else
                {
                    int position = 0;
                    foreach (PropertyDescriptor pd in ChoTypeDescriptor.GetProperties(recordType))
                    {
                        //if (!pd.PropertyType.IsSimple())
                        //    throw new ChoRecordConfigurationException("Property '{0}' is not a simple type.".FormatString(pd.Name));

                        var obj = new ChoCSVRecordFieldConfiguration(pd.Name, ++position);
                        obj.FieldType = pd.PropertyType;
                        CSVRecordFieldConfigurations.Add(obj);
                    }
                }
            }
        }

        public override void Validate(object state)
        {
            base.Validate(state);

            if (Delimiter.IsNull())
                throw new ChoRecordConfigurationException("Delimiter can't be null or whitespace.");
            if (Delimiter == EOLDelimiter)
                throw new ChoRecordConfigurationException("Delimiter [{0}] can't be same as EODDelimiter [{1}]".FormatString(Delimiter, EOLDelimiter));
            if (Delimiter.Contains(QuoteChar))
                throw new ChoRecordConfigurationException("QuoteChar [{0}] can't be one of Delimiter characters [{1}]".FormatString(QuoteChar, Delimiter));
            if (Comments != null && Comments.Contains(Delimiter))
                throw new ChoRecordConfigurationException("One of the Comments contains Delimiter. Not allowed.");

            //Validate Header
            if (FileHeaderConfiguration != null)
            {
                if (FileHeaderConfiguration.FillChar != null)
                {
                    ValidateChar(FileHeaderConfiguration.FillChar.Value, nameof(FileHeaderConfiguration.FillChar));
                }
            }

            string[] headers = state as string[];
            if (AutoDiscoverColumns
                && CSVRecordFieldConfigurations.Count == 0)
            {
                if (headers != null)
                {
                    int index = 0;
                    CSVRecordFieldConfigurations = (from header in headers
                                                    select new ChoCSVRecordFieldConfiguration(header, ++index)).ToList();
                }
                else
                {
                    MapRecordFields(RecordType);
                }
            }
            else
            {
                int maxFieldPos = CSVRecordFieldConfigurations.Max(r => r.FieldPosition);
                foreach (var fieldConfig in CSVRecordFieldConfigurations)
                {
                    if (fieldConfig.FieldPosition > 0) continue;
                    fieldConfig.FieldPosition = ++maxFieldPos;
                }
            }

            if (CSVRecordFieldConfigurations.Count > 0)
                MaxFieldPosition = CSVRecordFieldConfigurations.Max(r => r.FieldPosition);
            else
                throw new ChoRecordConfigurationException("No record fields specified.");

            //Validate each record field
            foreach (var fieldConfig in CSVRecordFieldConfigurations)
                fieldConfig.Validate(this);

            //Check if any field has 0 
            if (CSVRecordFieldConfigurations.Where(i => i.FieldPosition <= 0).Count() > 0)
                throw new ChoRecordConfigurationException("Some fields contain invalid field position. All field positions must be > 0.");

            //Check field position for duplicate
            int[] dupPositions = CSVRecordFieldConfigurations.GroupBy(i => i.FieldPosition)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key).ToArray();

            if (dupPositions.Length > 0)
                throw new ChoRecordConfigurationException("Duplicate field position(s) [Index: {0}] found.".FormatString(String.Join(",", dupPositions)));

            if (!FileHeaderConfiguration.HasHeaderRecord)
            {
            }
            else
            {
                //Check if any field has empty names 
                if (CSVRecordFieldConfigurations.Where(i => i.FieldName.IsNullOrWhiteSpace()).Count() > 0)
                    throw new ChoRecordConfigurationException("Some fields has empty field name specified.");

                //Check field names for duplicate
                string[] dupFields = CSVRecordFieldConfigurations.GroupBy(i => i.FieldName, FileHeaderConfiguration.StringComparer)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key).ToArray();

                if (dupFields.Length > 0)
                    throw new ChoRecordConfigurationException("Duplicate field name(s) [Name: {0}] found.".FormatString(String.Join(",", dupFields)));
            }

            RecordFieldConfigurationsDict = CSVRecordFieldConfigurations.OrderBy(i => i.FieldPosition).Where(i => !i.Name.IsNullOrWhiteSpace()).ToDictionary(i => i.Name, FileHeaderConfiguration.StringComparer);
            RecordFieldConfigurationsDict2 = CSVRecordFieldConfigurations.OrderBy(i => i.FieldPosition).Where(i => !i.FieldName.IsNullOrWhiteSpace()).ToDictionary(i => i.FieldName, FileHeaderConfiguration.StringComparer);
            AlternativeKeys = RecordFieldConfigurationsDict2.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Name, FileHeaderConfiguration.StringComparer);
            FCArray = RecordFieldConfigurationsDict.ToArray();

            LoadNCacheMembers(CSVRecordFieldConfigurations);

            if (Sanitize)
            {
                ValidateChar(InjectionEscapeChar, nameof(InjectionEscapeChar));
                foreach (char injectionChar in InjectionChars)
                {
                    ValidateChar(injectionChar, nameof(InjectionChars));
                    if (injectionChar.ToString().IsAlphaNumeric())
                        throw new ChoRecordConfigurationException("Invalid '{0}' injection char specified.".FormatString(injectionChar));
                }
            }
        }

        private void ValidateChar(char src, string name)
        {
            if (src == ChoCharEx.NUL)
                throw new ChoRecordConfigurationException("Invalid 'NUL' {0} specified.".FormatString(name));
            if (Delimiter.Contains(src))
                throw new ChoRecordConfigurationException("{2} [{0}] can't be one of Delimiter characters [{1}]".FormatString(FileHeaderConfiguration.FillChar, Delimiter, name));
            if (EOLDelimiter.Contains(src))
                throw new ChoRecordConfigurationException("{2} [{0}] can't be one of EOLDelimiter characters [{1}]".FormatString(src, EOLDelimiter, name));
            if ((from comm in Comments
                 where comm.Contains(src.ToString())
                 select comm).Any())
                throw new ChoRecordConfigurationException("One of the Comments contains {0}. Not allowed.".FormatString(name));
        }
    }
}
