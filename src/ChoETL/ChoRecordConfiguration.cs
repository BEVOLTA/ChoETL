﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

#if !NETSTANDARD2_0
using System.Windows.Data;
#endif

namespace ChoETL
{
    [DataContract]
    public abstract class ChoRecordConfiguration
    {
        protected Lazy<object> _recObject;
        private ChoTypeConverterFormatSpec _typeConverterFormatSpec = null;

        public char? ItemSeparator
        {
            get;
            set;
        }
        public Func<object, bool> Validator
        {
            get;
            set;
        }
        public ChoTypeConverterFormatSpec TypeConverterFormatSpec
        {
            get { return _typeConverterFormatSpec == null ? ChoTypeConverterFormatSpec.Instance : _typeConverterFormatSpec; }
            set { _typeConverterFormatSpec = value; }
        }
        private ChoFieldTypeAssessor _fieldTypeAssessor = null;
        public ChoFieldTypeAssessor FieldTypeAssessor
        {
            get { return _fieldTypeAssessor == null ? ChoFieldTypeAssessor.Instance : _fieldTypeAssessor; }
            set { _fieldTypeAssessor = value; }
        }
        public Type RecordType
        {
            get;
            set;
        }

        private Type _recordMapType;
        public Type RecordMapType
        {
            get { return _recordMapType == null ? RecordType : _recordMapType; }
            set { _recordMapType = value; }
        }

        [DataMember]
        public ChoErrorMode? ErrorMode
        {
            get;
            set;
        }
        [DataMember]
        public ChoIgnoreFieldValueMode? IgnoreFieldValueMode
        {
            get;
            set;
        }
        [DataMember]
        public bool AutoDiscoverColumns
        {
            get;
            set;
        }
        [DataMember]
        public bool ThrowAndStopOnMissingField
        {
            get;
            set;
        }
        [DataMember]
        public ChoObjectValidationMode ObjectValidationMode
        {
            get;
            set;
        }
        public Type SourceType
        {
            get;
            set;
        }
        [DataMember]
        public long NotifyAfter { get; set; }

        private bool _isDynamicObject = true;
        public virtual bool IsDynamicObject
        {
            get { return _isDynamicObject; }
            set { _isDynamicObject = value; }
        }

        public Dictionary<string, PropertyInfo> PIDict = null;
        public Dictionary<string, PropertyDescriptor> PDDict = null;
        internal bool HasConfigValidators = false;
        internal Dictionary<string, ValidationAttribute[]> ValDict = null;
        internal string[] PropertyNames;
        private HashSet<string> _ignoredFields = new HashSet<string>();
        public HashSet<string> IgnoredFields
        {
            get { return _ignoredFields; }
            set
            {
                if (value != null)
                    _ignoredFields = value;
                else
                    _ignoredFields.Clear();
            }
        }
        public abstract IEnumerable<ChoRecordFieldConfiguration> RecordFieldConfigurations
        {
            get;
        }

        internal ChoRecordConfiguration(Type recordType = null)
        {
            RecordType = recordType.GetUnderlyingType();
            ErrorMode = ChoErrorMode.ThrowAndStop; //  ChoErrorMode.ReportAndContinue;
            AutoDiscoverColumns = true;
            ThrowAndStopOnMissingField = true;
            ObjectValidationMode = ChoObjectValidationMode.Off;
            IsDynamicObject = RecordType.IsDynamicType();
        }

        protected virtual void Init(Type recordType)
        {
            if (recordType == null)
                return;

            var tc = recordType.GetCustomAttribute(typeof(ChoTypeConverterAttribute)) as ChoTypeConverterAttribute;
            if (tc != null)
            {
                var c = tc.CreateInstance();
                if (c is IChoValueConverter)
                    ChoTypeConverter.Global.Add(recordType, c as IChoValueConverter);
                else if (c is TypeConverter)
                    ChoTypeConverter.Global.Add(recordType, c as TypeConverter);
#if !NETSTANDARD2_0
                else if (c is IValueConverter)
                    ChoTypeConverter.Global.Add(recordType, c as IValueConverter);
#endif
            }

            var st = recordType.GetCustomAttribute(typeof(ChoSourceTypeAttribute)) as ChoSourceTypeAttribute;
            if (st != null)
            {
                SourceType = st.Type;
            }

            _recObject = new Lazy<object>(() => ChoActivator.CreateInstance(RecordType));
            ChoRecordObjectAttribute recObjAttr = ChoType.GetAttribute<ChoRecordObjectAttribute>(recordType);
            if (recObjAttr != null)
            {
                ErrorMode = recObjAttr.ErrorMode;
                IgnoreFieldValueMode = recObjAttr.IgnoreFieldValueModeInternal;
                ThrowAndStopOnMissingField = recObjAttr.ThrowAndStopOnMissingField;
                ObjectValidationMode = recObjAttr.ObjectValidationMode;
            }
        }

        //public abstract void MapRecordFields<T>();
        //public abstract void MapRecordFields(params Type[] recordTypes);
        public virtual void Validate(object state)
        {
            if (!IsDynamicObject)
            {
                //PIDict = ChoType.GetProperties(RecordType).ToDictionary(p => p.Name);
                PDDict = new Dictionary<string, PropertyDescriptor>(StringComparer.InvariantCultureIgnoreCase);
                if (PIDict != null)
                {
                    foreach (var fn in PIDict.Keys)
                        PDDict.Add(fn, ChoTypeDescriptor.GetProperty(RecordType, fn));
                }
            }
        }

        internal void LoadFieldConfigurationAttributesInternal(ChoRecordFieldConfiguration fc, Type reflectedType)
        {
            LoadFieldConfigurationAttributes(fc, reflectedType);
        }

        protected void LoadFieldConfigurationAttributes(ChoRecordFieldConfiguration fc, Type reflectedType)
        {
            if (!IsDynamicObject)
            {
                if (fc.PD != null && fc.PI != null)
                    return;
                    
                var recordType = reflectedType; // fc.ReflectedType == null ? RecordType : fc.ReflectedType;

                string name = null;
                object defaultValue = null;
                object fallbackValue = null;
                name = fc.Name;

                fc.ReflectedType = reflectedType;
                fc.PD = ChoTypeDescriptor.GetProperty(recordType, name);
                fc.PI = ChoType.GetProperty(recordType, name);

                if (fc.PD == null || fc.PI == null)
                    return;

                //Load default value
                defaultValue = ChoType.GetRawDefaultValue(fc.PD);
                if (defaultValue != null)
                {
                    fc.DefaultValue = defaultValue;
                    fc.IsDefaultValueSpecified = true;
                }
                //Load fallback value
                fallbackValue = ChoType.GetRawFallbackValue(fc.PD);
                if (fallbackValue != null)
                {
                    fc.FallbackValue = fallbackValue;
                    fc.IsFallbackValueSpecified = true;
                }

                //Load Converters
                fc.PropConverters = ChoTypeDescriptor.GetTypeConverters(fc.PI);
                fc.PropConverterParams = ChoTypeDescriptor.GetTypeConverterParams(fc.PI);

                //Load Custom Serializer
                fc.PropCustomSerializer = ChoTypeDescriptor.GetCustomSerializer(fc.PI);
                fc.PropCustomSerializerParams = ChoTypeDescriptor.GetCustomSerializerParams(fc.PI);

                if (fc.SourceType == null)
                    fc.SourceType = fc.GetSourceTypeFromConvertersIfAny();
            }
        }
        protected virtual void LoadNCacheMembers(IEnumerable<ChoRecordFieldConfiguration> fcs)
        {
            if (!IsDynamicObject)
            {
                string name = null;
                object defaultValue = null;
                object fallbackValue = null;
                foreach (var fc in fcs)
                {
                    //if (fc is ChoFileRecordFieldConfiguration)
                    //    name = ((ChoFileRecordFieldConfiguration)fc).FieldName;
                    //else
                    name = fc.Name;

                    fc.PD = PDDict.ContainsKey(name) ? PDDict[name] :
                        (PDDict.Any(p => p.Value.Name == name) ? PDDict.Where(p => p.Value.Name == name).Select(p => p.Value).FirstOrDefault() : null);
                    fc.PI = PIDict.ContainsKey(name) ? PIDict[name] :
           (PIDict.Any(p => p.Value.Name == name) ? PIDict.Where(p => p.Value.Name == name).Select(p => p.Value).FirstOrDefault() : null);

                    if (fc.PD == null || fc.PI == null)
                        continue;

                    //Load default value
                    defaultValue = ChoType.GetRawDefaultValue(fc.PD);
                    if (defaultValue != null)
                    {
                        fc.DefaultValue = defaultValue;
                        fc.IsDefaultValueSpecified = true;
                    }
                    //Load fallback value
                    fallbackValue = ChoType.GetRawFallbackValue(fc.PD);
                    if (fallbackValue != null)
                    {
                        fc.FallbackValue = fallbackValue;
                        fc.IsFallbackValueSpecified = true;
                    }

                    //Load Converters
                    fc.PropConverters = ChoTypeDescriptor.GetTypeConverters(fc.PI);
                    fc.PropConverterParams = ChoTypeDescriptor.GetTypeConverterParams(fc.PI);

                    //Load Custom Serializer
                    fc.PropCustomSerializer = ChoTypeDescriptor.GetCustomSerializer(fc.PI);
                    fc.PropCustomSerializerParams = ChoTypeDescriptor.GetCustomSerializerParams(fc.PI);

                    if (fc.SourceType == null)
                        fc.SourceType = fc.GetSourceTypeFromConvertersIfAny();
                }

                PropertyNames = PDDict.Keys.ToArray();
            }

            //Validators
            HasConfigValidators = (from fc in fcs
                                   where fc.Validators != null
                                   select fc).FirstOrDefault() != null;

            if (!HasConfigValidators)
            {
                if (!IsDynamicObject)
                {
                    string name = null;
                    foreach (var fc in fcs)
                    {
                        if (fc is ChoFileRecordFieldConfiguration)
                            name = ((ChoFileRecordFieldConfiguration)fc).FieldName;
                        else
                            name = fc.Name;

                        if (!PDDict.ContainsKey(name))
                            continue;
                        fc.Validators = ChoTypeDescriptor.GetPropetyAttributes<ValidationAttribute>(fc.PD).ToArray();
                    }
                }
            }

            ValDict = (from fc in fcs
                       select new KeyValuePair<string, ValidationAttribute[]>(fc is ChoFileRecordFieldConfiguration ? ((ChoFileRecordFieldConfiguration)fc).FieldName : fc.Name, fc.Validators))
                       .GroupBy(i => i.Key).Select(g => g.First()).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
        protected virtual void Clone(ChoRecordConfiguration config)
        {
            if (config == null)
                return;

            config.ErrorMode = ErrorMode;
            config.IgnoreFieldValueMode = IgnoreFieldValueMode;
            config.AutoDiscoverColumns = AutoDiscoverColumns;
            config.ThrowAndStopOnMissingField = ThrowAndStopOnMissingField;
            config.ObjectValidationMode = ObjectValidationMode;
            config.NotifyAfter = NotifyAfter;
        }

        private readonly object _typeTypeConverterCacheLock = new object();
        private readonly Dictionary<Type, object[]> _typeTypeConverterCache = new Dictionary<Type, object[]>();
        private readonly Dictionary<Type, object[]> _typeTypeConverterParamsCache = new Dictionary<Type, object[]>();

        public object[] GetConvertersForType(Type fieldType)
        {
            if (fieldType == null) return null;

            if (_typeTypeConverterCache.ContainsKey(fieldType))
                return _typeTypeConverterCache[fieldType];

            lock (_typeTypeConverterCacheLock)
            {
                if (_typeTypeConverterCache.ContainsKey(fieldType))
                    return _typeTypeConverterCache[fieldType];
                else
                {
                    return ChoTypeDescriptor.GetTypeConvertersForType(fieldType);
                }
            }
        }

        public object[] GetConverterParamsForType(Type fieldType)
        {
            if (fieldType == null) return null;

            if (_typeTypeConverterParamsCache.ContainsKey(fieldType))
                return _typeTypeConverterParamsCache[fieldType];

            lock (_typeTypeConverterCacheLock)
            {
                if (_typeTypeConverterParamsCache.ContainsKey(fieldType))
                    return _typeTypeConverterParamsCache[fieldType];
                else
                {
                    return ChoTypeDescriptor.GetTypeConverterParamsForType(fieldType);
                }
            }
        }

        public void ClearTypeConvertersForType<T>()
        {
            ClearTypeConvertersForType(typeof(T));
        }
        public void ClearTypeConvertersForType(Type objType)
        {
            if (objType == null)
                return;
            if (!_typeTypeConverterCache.ContainsKey(objType))
                return;

            lock (_typeTypeConverterCacheLock)
            {
                if (!_typeTypeConverterCache.ContainsKey(objType))
                    return;

                _typeTypeConverterCache.Remove(objType);
            }
        }
        public void RegisterTypeConvertersForType(Type objType, object[] converters)
        {
            if (objType == null)
                return;
            if (converters == null)
                converters = new object[] { };

            lock (_typeTypeConverterCacheLock)
            {
                if (!_typeTypeConverterCache.ContainsKey(objType))
                    _typeTypeConverterCache.Add(objType, converters);
                else
                    _typeTypeConverterCache[objType] = converters;
            }
        }
#if !NETSTANDARD2_0

        public void RegisterTypeConverterForType<T>(IValueConverter converter)
        {
            RegisterTypeConverterForTypeInternal(typeof(T), (object)converter);
        }
#endif
        public void RegisterTypeConverterForType<T>(IChoValueConverter converter)
        {
            RegisterTypeConverterForTypeInternal(typeof(T), (object)converter);
        }
#if !NETSTANDARD2_0

        public void RegisterTypeConverterForType(Type objType, IValueConverter converter)
        {
            RegisterTypeConverterForTypeInternal(objType, (object)converter);
        }
#endif
        public void RegisterTypeConverterForType(Type objType, IChoValueConverter converter)
        {
            RegisterTypeConverterForTypeInternal(objType, (object)converter);
        }

        private void RegisterTypeConverterForTypeInternal(Type objType, object converter)
        {
            if (objType == null)
                return;
            if (converter == null)
                return;

            lock (_typeTypeConverterCacheLock)
            {
                if (!_typeTypeConverterCache.ContainsKey(objType))
                    _typeTypeConverterCache.Add(objType, new object[] { converter });
                else
                    _typeTypeConverterCache[objType] = _typeTypeConverterCache[objType].Union(new object[] { converter }).ToArray();
            }
        }
    }
}
