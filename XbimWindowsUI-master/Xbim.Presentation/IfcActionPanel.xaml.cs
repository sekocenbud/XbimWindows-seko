using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Xbim.Common;
using System.Windows.Controls;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Configuration;
using System.Threading;
using System.IO;
using System.Diagnostics;
using ConsoleTcpClient;
using System.Windows.Data;
using System.Windows.Input;
using Xbim.Presentation;
using Xbim.Common.Metadata;
using static Xbim.Presentation.IfcMetaDataControl;

namespace Xbim.Presentation
{
	public partial class IfcActionPanel : INotifyPropertyChanged
	{
		#region Private fields

		private enum spxCommand { askGUID, locGUID, addGUID, noneMsg };
		private TcpClient _tcpclnt = new TcpClient();
		private Stream _stm;
		private int _tcpPort = 0;
		private bool _showDiagMsg = false;
		private string _msgToSend = string.Empty;
		private string _fieldGUID = string.Empty;
		private string _locGUID = string.Empty;
		private IPersistEntity _entity;
		//private IfcStore _model;
        private string _communicationDataSeparator = string.Empty;
        private string _communicationDataGroupSeparator = string.Empty;

        #endregion Private fields

        #region Properties

        private string _tcpMsg;

		public string tcpMsg
		{
			get
			{
				return _tcpMsg;
			}
			set
			{
				OnPropertyChanged("tcpMsg");
				_tcpMsg = value;
			}
		}

		private string _fileName;

		public string fileName
		{
			get
			{
				return _fileName;
			}
			set
			{
				OnPropertyChanged("fileName");
				_fileName = value;
			}
		}

		private void OnPropertyChanged(String property)
		{
			if (PropertyChanged != null)
			{
				PropertyChanged(this, new PropertyChangedEventArgs(property));
			}
		}

		#endregion Properties

		#region Dependency property

		public IfcStore Model
		{
			get { return (IfcStore)GetValue(ModelProperty); }
			set { SetValue(ModelProperty, value); }
		}

		// Using a DependencyProperty as the backing store for Model.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty ModelProperty =
		    DependencyProperty.Register("Model", typeof(IfcStore), typeof(IfcActionPanel),
			new PropertyMetadata(null, OnModelChanged));

		private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var ctrl = d as IfcActionPanel;
			if (ctrl == null)
				return;
			if (e.NewValue == null)
			{
				ctrl.Clear();
			}
			ctrl.DataRebind(null);
		}

		public IPersistEntity SelectedEntity
		{
			get { return (IPersistEntity)GetValue(SelectedEntityProperty); }
			set { SetValue(SelectedEntityProperty, value); }
		}

		// Using a DependencyProperty as the backing store for IfcInstance.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty SelectedEntityProperty =
		    DependencyProperty.Register("SelectedEntity", typeof(IPersistEntity), typeof(IfcActionPanel),
			new UIPropertyMetadata(null, OnSelectedEntityChanged));


		private static void OnSelectedEntityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var ctrl = d as IfcActionPanel;
			if (ctrl != null && e.NewValue is IPersistEntity)
			{
				ctrl.DataRebind((IPersistEntity)e.NewValue);
			}
		}

		#endregion Dependency property

		#region ctor

		public IfcActionPanel()
		{
			InitializeComponent();

			this.fieldCommunication.DataContext = this.tcpMsg;

			_tcpPort = int.Parse(ConfigurationSettings.AppSettings["TCPListeningPort"]);
			_showDiagMsg = bool.Parse(ConfigurationSettings.AppSettings["ShowDiagnosticMsg"]);
            _communicationDataSeparator = ConfigurationSettings.AppSettings["CommunicationDataSeparator"];
            _communicationDataGroupSeparator = ConfigurationSettings.AppSettings["CommunicationDataGroupSeparator"];

            ConsoleTCPClient tcpConsoleReader = new ConsoleTCPClient(_tcpPort, this);
			tcpConsoleReader.StartClient();

			_tcpclnt = new TcpClient();
			_tcpclnt.Connect("localhost", _tcpPort);
			_stm = _tcpclnt.GetStream();
		}

		#endregion ctor

		#region Communication Requests

		public void RequestOpenModel()
		{
			//call public void LoadAnyModel(string modelFileName)
			//from MainWindow
		}

		public string RquestAskGUID()
		{
			if (_fieldGUID != "")
			{
				if (_showDiagMsg)
				{
					MessageBoxResult result = MessageBox.Show(string.Format("dotarl request askGUID.\naktualny GUDIO:{0}", _fieldGUID));
				}
				return BuildTransmissionMessage("askGUID", _fieldGUID);
			}

			else
			{
				if (_showDiagMsg)
				{
					MessageBoxResult result = MessageBox.Show(string.Format("dotarl request askGUID. aktualny GUDIO: {0}", "not found"));
				}
				return BuildTransmissionMessage("askGUID", "not found");
			}
		}

		public string RequestLocGUID(string msg)
		{
			IPersistEntity element = (IPersistEntity)null;

			string guid = msg.Replace("Prix!locGUID!22!", "").Replace("\0", "");

			if (guid != "")
			{
				element = findElementByGuid(guid, true);
			}
			if (_showDiagMsg)
			{
				MessageBoxResult result = MessageBox.Show(string.Format("dotarl request lokGUID wraz z GUID: {0}\n\n{1} o tym numerze GUID", guid, element != null ? "Znaleziono element" : "Nie znaleziono elementu"));
			}

			if (element != null)
			{
				_locGUID = guid;

				FillActionPanelData();

			}
			else
			{
				_locGUID = string.Empty;
			}

			return (element != null) ? BuildTransmissionMessage("locGUID", "OK") : BuildTransmissionMessage("locGUID", "not found");
		}

		public string RequestOpenFile(string msg)
		{
            fileName = string.Empty;

			string guid = msg.Replace("Prix!open!", "").Replace("\0", "");

			int pos = guid.IndexOf("!");

			fileName = guid.Substring(pos+1);

			//czyszczenie pol
			this.Dispatcher.Invoke(() =>
			{
                fieldWysokosc.Text = string.Empty;
				fieldSzerokosc.Text = string.Empty;
                fieldObjetosc.Text = string.Empty;
                fieldPowierzchnia.Text = string.Empty;
                fieldObliczenia.Text = string.Empty;
            });

			OnLoadFileExecute();

			return "";
		}

		public string RequestAddGUID()
		{
			return "";
		}

		private void SendData(spxCommand command, string msg)
		{
			this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
			    (ThreadStart)(delegate ()
			  {
				  try
				  {
					  string str = msg;

					  ASCIIEncoding asen = new ASCIIEncoding();
					  byte[] ba = asen.GetBytes(str);
					   _stm.Write(ba, 0, ba.Length);
					  Debug.WriteLine(string.Format("Sending message: {0}", msg));
				   }

				  catch (Exception err)
				  {
					  Debug.WriteLine(string.Format("Error: {0}", err.StackTrace));
				   }
			  }));

		}

		private string BuildTransmissionMessage(string id, string msg)
		{
			return string.Format("{0}{1}", id, msg);
		}

		private IPersistEntity findElementByGuid(string guid, bool change)
		{
			IPersistEntity element = null;

			this.Dispatcher.Invoke(() =>
			{
				element = Model.Instances.OfType<IIfcRoot>().Where(i => i.GlobalId.Value.ToString().Contains(guid)).FirstOrDefault();

				if (element != null)
					SelectedEntity = (IPersistEntity)element;

			});

			return (IPersistEntity)element;
		}

		#endregion Communication Rwquests

		#region INotifyPropertyChanged Members

		private void ActionPane_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (e.AddedItems.Count <= 0)
				return;
			//var selectedTab = e.AddedItems[0] as TabItem;
			FillActionPanelData();
		}

		public event PropertyChangedEventHandler PropertyChanged;

		private void NotifyPropertyChanged(string info)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(info));
		}

		#endregion INotifyPropertyChanged Members

		#region EventHandlers for Buttons

		public event EventHandler LoadFileExecute;

		protected void OnLoadFileExecute()
		{
			if (LoadFileExecute != null)
				LoadFileExecute(this, EventArgs.Empty);
		}

		public event EventHandler BtnZoomInExecute;

		protected void OnBtnZoomInExecute()
		{
			if (BtnZoomInExecute != null)
				BtnZoomInExecute(this, EventArgs.Empty);
		}

		public event EventHandler BtnShowAll;

		protected void OnBtnShowAll()
		{
			if (BtnShowAll != null)
				BtnShowAll(this, EventArgs.Empty);
		}

		#endregion EventHandlers for Buttons

		#region Buttons Actions

		private void Button_Click(object sender, RoutedEventArgs e)
		{
			this.fieldObliczenia.Text += this.fieldWysokosc.Text + " ";
		}

		private void BtnSzerokosc_Click(object sender, RoutedEventArgs e)
		{
			this.fieldObliczenia.Text += this.fieldSzerokosc.Text + " ";
		}

		private void BtnObjetosc_Click(object sender, RoutedEventArgs e)
		{
			this.fieldObliczenia.Text += this.fieldObjetosc.Text + " ";
		}

		private void BtnPowierzchnia_Click(object sender, RoutedEventArgs e)
		{
			this.fieldObliczenia.Text += this.fieldObjetosc.Text + " ";
		}

		private void BtnWstawDoKalkulacji_Click(object sender, RoutedEventArgs e)
		{
			string properties = string.Empty;

            string allProperties = GetAllProperties();
            string allObjectData = GetAllObjectData();
            string allTypeData = GetAllTypeData();
            string allMaterialData = GetAllMaterialData();
            string allQuantityData = GetAllQuantityData();

            properties = allProperties + allObjectData + allTypeData + allMaterialData + allQuantityData;

            string msg = BuildTransmissionMessage("addGUID", string.Format("{0}{4}{1}{4}{2}{4}{3}", "{" + this._fieldGUID + "}", fieldObliczenia.Text, ((IIfcRoot)_entity).Name, properties, _communicationDataSeparator));

            SendData(spxCommand.addGUID, msg);
			this.fieldCommunication.Text = "> " + msg;
		}

		private void BtnUstaw_Click(object sender, RoutedEventArgs e)
		{
			string msg = BuildTransmissionMessage("locGUID", string.Format("{0}", "{" + this._fieldGUID + "}"));

			SendData(spxCommand.locGUID, msg);
			this.fieldCommunication.Text = "> " + msg;
		}

		private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
		{
			OnBtnZoomInExecute();
		}

		private void BtnShowAll_Click(object sender, RoutedEventArgs e)
		{
			OnBtnShowAll();
		}

		#endregion Buttons Actions

		#region Helpers

		private void FillActionPanelData()
		{
			if (_entity == null)
				return;

            this.Dispatcher.Invoke(() =>
            {
                fieldObliczenia.Text = string.Empty;
            });

            var ifcType = _entity.ExpressType.Properties.Values;
			if (ifcType.Count != 0)
			{
				var propVal = string.Empty;

				var propItem = ifcType.Where(i => i.Name == "OverallHeight").FirstOrDefault();
				if (propItem != null)
					this.Dispatcher.Invoke(() =>
					{
						fieldWysokosc.Text = string.Format("{0:### ##0.0000#}", Convert.ToDouble(propItem.PropertyInfo.GetValue(_entity, null).ToString()));
					});
				else
					this.Dispatcher.Invoke(() =>
					{
						fieldWysokosc.Text = "";
					});


				propItem = ifcType.Where(i => i.Name == "OverallWidth").FirstOrDefault();
				if (propItem != null)
					this.Dispatcher.Invoke(() =>
					{
						this.fieldSzerokosc.Text = string.Format("{0:### ##0.0000#}", Convert.ToDouble(propItem.PropertyInfo.GetValue(_entity, null).ToString()));
					});
				else
					this.Dispatcher.Invoke(() =>
					{
						this.fieldSzerokosc.Text = "";
					});
			}
			else
			{
				this.Dispatcher.Invoke(() =>
				{
					this.fieldWysokosc.Text = "";
					this.fieldSzerokosc.Text = "";
				});
			}

			var root = _entity as IIfcRoot;
			if (root == null)
				return;

			this._fieldGUID = root.GlobalId;

			if (_entity is IIfcObject)
			{
				var asIfcObject = (IIfcObject)_entity;
				var pSetq = asIfcObject.IsDefinedBy.Select(i => i.RelatingPropertyDefinition as IIfcPropertySet).ToList();
				var pSubSet = pSetq.Where(i => i.Name == "Wymiary").ToList();
				if (pSubSet.Count != 0)
				{
					var pSetSingle = pSubSet?.FirstOrDefault().HasProperties.ToList();
					if (pSetSingle != null)
					{
						var val = pSetSingle.Where(i => i.Name == "Objętość").FirstOrDefault() as IIfcPropertySingleValue;
						if (val != null)
							this.Dispatcher.Invoke(() =>
							{
								this.fieldObjetosc.Text = string.Format("{0:### ##0.0000#}", Convert.ToDouble(val.NominalValue.ToString()));
							});
						else
							this.Dispatcher.Invoke(() =>
							{
								this.fieldObjetosc.Text = "";
							});

						val = pSetSingle.Where(i => i.Name == "Powierzchnia").FirstOrDefault() as IIfcPropertySingleValue;
						if (val != null)
							this.Dispatcher.Invoke(() =>
							{
								this.fieldPowierzchnia.Text = string.Format("{0:### ##0.0000#}", Convert.ToDouble(val.NominalValue.ToString()));
							});
						else
							this.Dispatcher.Invoke(() =>
							{
								this.fieldPowierzchnia.Text = "";
							});
					}
				}
				else
				{
					this.Dispatcher.Invoke(() =>
					{
						this.fieldObjetosc.Text = "";
						this.fieldPowierzchnia.Text = "";
					});
				}
			}
		}

		private void DataRebind(IPersistEntity entity)
		{
			Clear(); //remove any bindings
			_entity = null;
			if (entity != null)
			{
				_entity = entity;
				FillActionPanelData();
			}
			else
				_entity = null;
		}

		private void Clear()
		{
			NotifyPropertyChanged("Properties");
			NotifyPropertyChanged("PropertySets");
		}

		private string GetAllProperties()
		{
			string properties = string.Empty;

			//if (_properties.Any()) //don't try to fill unless empty
			//	return;
			//now the property sets for any 

			if (_entity is IIfcObject)
			{
				var asIfcObject = (IIfcObject)_entity;
				foreach (
				    var pSet in
					asIfcObject.IsDefinedBy.Select(
					    relDef => relDef.RelatingPropertyDefinition as IIfcPropertySet)
				    )
					properties += AddPropertySet(pSet);
			}
			else if (_entity is IIfcTypeObject)
			{
				var asIfcTypeObject = _entity as IIfcTypeObject;
				if (asIfcTypeObject.HasPropertySets == null)
					return "";
				foreach (var pSet in asIfcTypeObject.HasPropertySets.OfType<IIfcPropertySet>())
				{
					properties += AddPropertySet(pSet);
				}
			}
			return properties;
		}

		private string AddPropertySet(IIfcPropertySet pSet)
		{
			string properties = string.Empty;

			if (pSet == null)
				return "";

			foreach (var item in pSet.HasProperties.OfType<IIfcPropertySingleValue>()) //handle IfcPropertySingleValue
			{
				properties += AddProperty(item, pSet.Name);
			}
			foreach (var item in pSet.HasProperties.OfType<IIfcComplexProperty>()) // handle IfcComplexProperty
			{
				// by invoking the undrlying addproperty function with a longer path
				foreach (var composingProperty in item.HasProperties.OfType<IIfcPropertySingleValue>())
				{
					properties += AddProperty(composingProperty, pSet.Name + " / " + item.Name);
				}
			}

			return properties;
		}

		private string AddProperty(IIfcPropertySingleValue item, string groupName)
		{
			var val = "";
			var nomVal = item.NominalValue;
			if (nomVal != null)
				val = nomVal.ToString();

			return string.Format("{3}{0}{4}{1}{4}{2}", groupName, item.Name, val, _communicationDataGroupSeparator, _communicationDataSeparator);
		}

        private string GetAllQuantityData()
        {
            string properties = string.Empty;

            var o = _entity as IIfcObject;
            if (o != null)
            {
                var ifcObj = o;
                var modelUnits = _entity.Model.Instances.OfType<IIfcUnitAssignment>().FirstOrDefault();
                // not optional, should never return void in valid model

                foreach (
                    var relDef in
                        ifcObj.IsDefinedBy.Where(r => r.RelatingPropertyDefinition is IIfcElementQuantity))
                {
                    var pSet = relDef.RelatingPropertyDefinition as IIfcElementQuantity;
                    properties += AddQuantityPSet(pSet, modelUnits);
                }
            }
            else if (_entity is IIfcTypeObject)
            {
                var asIfcTypeObject = _entity as IIfcTypeObject;
                var modelUnits = _entity.Model.Instances.OfType<IIfcUnitAssignment>().FirstOrDefault();

                if (asIfcTypeObject.HasPropertySets == null)
                    return properties;
                foreach (var pSet in asIfcTypeObject.HasPropertySets.OfType<IIfcElementQuantity>())
                {
                    properties += AddQuantityPSet(pSet, modelUnits);
                }
            }

            return properties;
        }

        private string AddQuantityPSet(IIfcElementQuantity pSet, IIfcUnitAssignment modelUnits)
        {
            string properties = string.Empty;

            if (pSet == null)
                return "" ;

            if (modelUnits == null) throw new ArgumentNullException(nameof(modelUnits));
            foreach (var item in pSet.Quantities.OfType<IIfcPhysicalSimpleQuantity>())

            {
                properties += string.Format("{3}{0}{4}{1}{4}{2}", pSet.Name, item.Name, GetValueString(item, modelUnits), _communicationDataGroupSeparator, _communicationDataSeparator);
            }

            return properties;
        }

        private static string GetValueString(IIfcPhysicalSimpleQuantity quantity, IIfcUnitAssignment modelUnits)
        {
            if (quantity == null)
                return "";
            string value = null;
            var u = quantity.Unit;
            if (u == null)
                return "";
            var unit = u.FullName;
            var length = quantity as IIfcQuantityLength;
            if (length != null)
            {
                value = length.LengthValue.ToString();
                if (quantity.Unit == null)
                    unit = GetUnit(modelUnits, IfcUnitEnum.LENGTHUNIT);
            }
            var area = quantity as IIfcQuantityArea;
            if (area != null)
            {
                value = area.AreaValue.ToString();
                if (quantity.Unit == null)
                    unit = GetUnit(modelUnits, IfcUnitEnum.AREAUNIT);
            }
            var weight = quantity as IIfcQuantityWeight;
            if (weight != null)
            {
                value = weight.WeightValue.ToString();
                if (quantity.Unit == null)
                    unit = GetUnit(modelUnits, IfcUnitEnum.MASSUNIT);
            }
            var time = quantity as IIfcQuantityTime;
            if (time != null)
            {
                value = time.TimeValue.ToString();
                if (quantity.Unit == null)
                    unit = GetUnit(modelUnits, IfcUnitEnum.TIMEUNIT);
            }
            var volume = quantity as IIfcQuantityVolume;
            if (volume != null)
            {
                value = volume.VolumeValue.ToString();
                if (quantity.Unit == null)
                    unit = GetUnit(modelUnits, IfcUnitEnum.VOLUMEUNIT);
            }
            var count = quantity as IIfcQuantityCount;
            if (count != null)
                value = count.CountValue.ToString();


            if (string.IsNullOrWhiteSpace(value))
                return "";

            return string.IsNullOrWhiteSpace(unit) ?
                value :
                $"{value} {unit}";
        }

        private static string GetUnit(IIfcUnitAssignment units, IfcUnitEnum type)
        {
            var unit = units?.Units.OfType<IIfcNamedUnit>().FirstOrDefault(u => u.UnitType == type);
            return unit?.FullName;
        }

        private string GetAllMaterialData()
        {
            string properties = string.Empty;

            if (_entity is IIfcObject)
            {
                var ifcObj = _entity as IIfcObject;
                var matRels = ifcObj.HasAssociations.OfType<IIfcRelAssociatesMaterial>();
                foreach (var matRel in matRels)
                {
                    properties += AddMaterialData(matRel.RelatingMaterial, "");
                }
            }
            else if (_entity is IIfcTypeObject)
            {
                var ifcObj = _entity as IIfcTypeObject;
                var matRels = ifcObj.HasAssociations.OfType<IIfcRelAssociatesMaterial>();
                foreach (var matRel in matRels)
                {
                    properties += AddMaterialData(matRel.RelatingMaterial, "");
                }
            }

            return properties;
        }

        private string AddMaterialData(IIfcMaterialSelect matSel, string setName)
        {
            string properties = string.Empty;

            if (matSel is IIfcMaterial) //simplest just add it
                properties += string.Format("{3}{0}{4}{1}{4}{2}", setName, $"{((IIfcMaterial)matSel).Name} [#{matSel.EntityLabel}]", ""
                    , _communicationDataGroupSeparator, _communicationDataSeparator);

            else if (matSel is IIfcMaterialLayer)
                properties += string.Format("{3}{0}{4}{1}{4}{2}", setName
                    , $"{((IIfcMaterialLayer)matSel).Material.Name} [#{matSel.EntityLabel}]"
                    , ((IIfcMaterialLayer)matSel).LayerThickness.Value.ToString()
                    , _communicationDataGroupSeparator, _communicationDataSeparator);

            else if (matSel is IIfcMaterialList)
            {
                foreach (var mat in ((IIfcMaterialList)matSel).Materials)
                {
                    properties += string.Format("{3}{0}{4}{1}{4}{2}", setName, $"{mat.Name} [#{mat.EntityLabel}]", "", _communicationDataGroupSeparator, _communicationDataSeparator);
                }
            }
            else if (matSel is IIfcMaterialLayerSet)
            {
                foreach (var item in ((IIfcMaterialLayerSet)matSel).MaterialLayers) //recursive call to add materials
                {
                    properties += AddMaterialData(item, ((IIfcMaterialLayerSet)matSel).LayerSetName);
                }
            }
            else if (matSel is IIfcMaterialLayerSetUsage)
            {
                foreach (var item in ((IIfcMaterialLayerSetUsage)matSel).ForLayerSet.MaterialLayers)
                {
                    properties += AddMaterialData(item, ((IIfcMaterialLayerSetUsage)matSel).ForLayerSet.LayerSetName);
                }
            }

            return properties;
        }

        private string GetAllObjectData()
        {
            string properties = string.Empty;

            if (_entity == null)
                return "";

            properties += string.Format("{3}{0}{4}{1}{4}{2}", "General", "Ifc Label", "#" + _entity.EntityLabel, _communicationDataGroupSeparator, _communicationDataSeparator);

            var ifcType = _entity.ExpressType;

            properties += string.Format("{3}{0}{4}{1}{4}{2}", "General", "Type", ifcType.Type.Name, _communicationDataGroupSeparator, _communicationDataSeparator);

            var ifcObj = _entity as IIfcObject;
            var typeEntity = ifcObj?.IsTypedBy.FirstOrDefault()?.RelatingType;
            if (typeEntity != null)
                properties += string.Format("{3}{0}{4}{1}{4}{2}", "General", "Defining Type", typeEntity.Name, _communicationDataGroupSeparator, _communicationDataSeparator);

            var props = ifcType.Properties.Values;
            foreach (var prop in props)
                properties += ReportProp(_entity, prop, true);

            var invs = ifcType.Inverses;

            foreach (var inverse in invs)
                properties += ReportProp(_entity, inverse, false);

            var root = _entity as IIfcRoot;
            if (root == null)
                return properties;

            properties += string.Format("{3}{0}{4}{1}{4}{2}", "OldUI", "Name", root.Name, _communicationDataGroupSeparator, _communicationDataSeparator);

            properties += string.Format("{3}{0}{4}{1}{4}{2}", "OldUI", "Description", root.Description, _communicationDataGroupSeparator, _communicationDataSeparator);

            properties += string.Format("{3}{0}{4}{1}{4}{2}", "OldUI", "GUID", root.GlobalId, _communicationDataGroupSeparator, _communicationDataSeparator);

            if (root.OwnerHistory != null)
            {
                string longValue = root.OwnerHistory.OwningUser + " using " +
                        root.OwnerHistory.OwningApplication.ApplicationIdentifier;

                properties += string.Format("{3}{0}{4}{1}{4}{2}", "OldUI", "Ownership", longValue, _communicationDataGroupSeparator, _communicationDataSeparator);
            }

            foreach (var pInfo in ifcType.Properties.Where
                (p => p.Value.EntityAttribute.Order > 4
                      && p.Value.EntityAttribute.State != EntityAttributeState.DerivedOverride)
                ) 
            {
                var val = pInfo.Value.PropertyInfo.GetValue(_entity, null);
                if (val == null || !(val is ExpressType))
                    continue;

                properties += string.Format("{3}{0}{4}{1}{4}{2}", "OldUI", pInfo.Value.PropertyInfo.Name, ((ExpressType)val).ToString(), _communicationDataGroupSeparator, _communicationDataSeparator);
            }

            return properties;
        }

        private string ReportProp(IPersistEntity entity, ExpressMetaProperty prop, bool verbose)
        {
            string properties = string.Empty;

            var propVal = prop.PropertyInfo.GetValue(entity, null);
            if (propVal == null)
            {
                if (!verbose)
                    return properties;

                propVal = "<null>";
            }

            if (prop.EntityAttribute.IsEnumerable)
            {
                var propCollection = propVal as IEnumerable<object>;

                if (propCollection != null)
                {
                    var propVals = propCollection.ToArray();

                    switch (propVals.Length)
                    {
                        case 0:
                            if (!verbose)
                                return properties;
                            properties += string.Format("{3}{0}{4}{1}{4}{2}", "General", prop.PropertyInfo.Name, "<empty>", _communicationDataGroupSeparator, _communicationDataSeparator);

                            break;
                        case 1:
                            var tmpSingle = GetPropItem(propVals[0]);
                            tmpSingle.Name = prop.PropertyInfo.Name + " (∞)";
                            tmpSingle.PropertySetName = "General";
                            properties += string.Format("{3}{0}{4}{1}{4}{2}", tmpSingle.PropertySetName, tmpSingle.Name, tmpSingle.Value, _communicationDataGroupSeparator, _communicationDataSeparator);
                            break;
                        default:
                            foreach (var item in propVals)
                            {
                                var tmpLoop = GetPropItem(item);
                                tmpLoop.Name = item.GetType().Name;
                                tmpLoop.PropertySetName = prop.PropertyInfo.Name;
                                properties += string.Format("{3}{0}{4}{1}{4}{2}", tmpLoop.PropertySetName, tmpLoop.Name, tmpLoop.Value, _communicationDataGroupSeparator, _communicationDataSeparator);
                            }
                            break;
                    }
                }
                else
                {
                    if (!verbose)
                        return properties;

                    properties += string.Format("{3}{0}{4}{1}{4}{2}", "General", prop.PropertyInfo.Name, "<not an enumerable>", _communicationDataGroupSeparator, _communicationDataSeparator);
                }
            }
            else
            {
                var tmp = GetPropItem(propVal);
                tmp.Name = prop.PropertyInfo.Name;
                tmp.PropertySetName = "General";

                properties += string.Format("{3}{0}{4}{1}{4}{2}", tmp.PropertySetName, tmp.Name, tmp.Value, _communicationDataGroupSeparator, _communicationDataSeparator);
            }

            return properties;
        }

        private PropertyItem GetPropItem(object propVal)
        {
            var retItem = new PropertyItem();

            var pe = propVal as IPersistEntity;
            var propLabel = 0;
            if (pe != null)
            {
                propLabel = pe.EntityLabel;
            }
            var ret = propVal.ToString();
            if (ret == propVal.GetType().FullName)
            {
                ret = propVal.GetType().Name;
            }

            retItem.Value = ret;
            retItem.IfcLabel = propLabel;

            return retItem;
        }

        private string GetAllTypeData()
        {
            string properties = string.Empty;

            var ifcObj = _entity as IIfcObject;
            var typeEntity = ifcObj?.IsTypedBy.FirstOrDefault()?.RelatingType;
            if (typeEntity == null)
                return properties;

            var ifcType = typeEntity?.ExpressType;

            properties += string.Format("{3}{0}{4}{1}{4}{2}", "TypeData", "Type", ifcType.Type.Name, _communicationDataGroupSeparator, _communicationDataSeparator);

            properties += string.Format("{3}{0}{4}{1}{4}{2}", "TypeData", "Ifc Label", "#" + typeEntity.EntityLabel, _communicationDataGroupSeparator, _communicationDataSeparator);

            properties += string.Format("{3}{0}{4}{1}{4}{2}", "TypeData", "Name", typeEntity.Name, _communicationDataGroupSeparator, _communicationDataSeparator);

            properties += string.Format("{3}{0}{4}{1}{4}{2}", "TypeData", "Description", typeEntity.Description, _communicationDataGroupSeparator, _communicationDataSeparator);

            properties += string.Format("{3}{0}{4}{1}{4}{2}", "TypeData", "GUID", typeEntity.GlobalId, _communicationDataGroupSeparator, _communicationDataSeparator);

            if (typeEntity.OwnerHistory != null)
            {
                string longValue = typeEntity.OwnerHistory.OwningUser + " using " +
                       typeEntity.OwnerHistory.OwningApplication.ApplicationIdentifier;

                properties += string.Format("{3}{0}{4}{1}{4}{2}", "TypeData", "Ownership", longValue, _communicationDataGroupSeparator, _communicationDataSeparator);
            }

            //now do properties in further specialisations that are text labels
            foreach (var pInfo in ifcType.Properties.Where
                (p => p.Value.EntityAttribute.Order > 4
                      && p.Value.EntityAttribute.State != EntityAttributeState.DerivedOverride)
                ) //skip the first for of root, and derived and things that are objects
            {
                var val = pInfo.Value.PropertyInfo.GetValue(typeEntity, null);
                if (!(val is ExpressType))
                    continue;

                properties += string.Format("{3}{0}{4}{1}{4}{2}", "TypeData", pInfo.Value.PropertyInfo.Name, ((ExpressType)val).ToString(), _communicationDataGroupSeparator, _communicationDataSeparator);
            }

            return properties;
        }

        #endregion Helpers
    }

	#region Converters : IValueConverter

	public class ListToStringConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			if (value != null)
			{
				var ifcObj = value as IIfcObject;
				return ifcObj.GlobalId.ToString();
			}
			else
				return "";
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			return ""; 
		}
	}

	#endregion Converters :IValueConverter
}
