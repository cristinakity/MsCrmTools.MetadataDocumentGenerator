﻿using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using MsCrmTools.MetadataDocumentGenerator.Forms;
using MsCrmTools.MetadataDocumentGenerator.Generation;
using MsCrmTools.MetadataDocumentGenerator.Helper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;
using System.Linq;

namespace MsCrmTools.MetadataDocumentGenerator
{
    public partial class MetadataDocumentGenerator : PluginControlBase, IGitHubPlugin
    {
        #region Variables

        private List<EntityMetadata> emdCache;

        private bool loadSettingsFlag = false;
        private GenerationSettings settings;

        #endregion Variables

        #region Constructor

        /// <summary>
        /// Initializes a new instance of class <see cref="MetadataDocumentGenerator"/>
        /// </summary>
        public MetadataDocumentGenerator()
        {
            InitializeComponent();

            cbbOutputType.SelectedIndex = 0;
            cbbSelectionType.SelectedIndex = 0;

            settings = new GenerationSettings();
            settings.AttributesSelection = AttributeSelectionOption.AllAttributes;
        }

        #endregion Constructor

        #region Methods

        public void LoadEntities(bool fromSolution = false)
        {
            List<Entity> solutions = new List<Entity>();

            if (fromSolution)
            {
                var dialog = new SolutionPicker(Service);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                solutions.AddRange(dialog.SelectedSolutions);
            }

            lvEntities.Items.Clear();
            cbbLcid.Items.Clear();

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading Entities...",
                Work = (bw, e) =>
                {
                    emdCache = new List<EntityMetadata>();

                    // Search for all entities metadata
                    foreach (var emd in MetadataHelper.GetEntities(solutions, Service))
                    {
                        emdCache.Add(emd);
                    }

                    bw.ReportProgress(0, "Retrieving available languages");

                    var lcidRequest = new RetrieveProvisionedLanguagesRequest();
                    var lcidResponse = (RetrieveProvisionedLanguagesResponse)Service.Execute(lcidRequest);

                    e.Result =
                        lcidResponse.RetrieveProvisionedLanguages.Select(
                            lcid => new LanguageCode { Lcid = lcid, Label = CultureInfo.GetCultureInfo(lcid).EnglishName });
                },
                PostWorkCallBack = e =>
                {
                    foreach (var emd in emdCache)
                    {
                        var displayName = emd.DisplayName != null && emd.DisplayName.UserLocalizedLabel != null
                            ? emd.DisplayName.UserLocalizedLabel.Label
                            : "N/A";
                        var name = emd.LogicalName;

                        lvEntities.Items.Add(new ListViewItem
                        {
                            Text = displayName,
                            SubItems =
                            {
                                name
                            },
                            Tag = name
                        });
                    }
                    
                    foreach (var lc in (IEnumerable<LanguageCode>)e.Result)
                    {
                        cbbLcid.Items.Add(lc);
                    }

                    if (cbbLcid.Items.Count > 0)
                    {
                        cbbLcid.SelectedIndex = 0;
                    }

                    SetWorkingState(false);
                },
                ProgressChanged = e => { SetWorkingMessage(e.UserState.ToString()); }
            });
        }

        private void SetWorkingState(bool isWorking)
        {
            gbOutput.Enabled = !isWorking;
            gbAttributeSelection.Enabled = !isWorking;
            gbOptions.Enabled = !isWorking;

            tssbLoadEntities.Enabled = !isWorking;
            tsbGenerate.Enabled = !isWorking;

            settingsToolStripDropDownButton.Enabled = !isWorking;
            rbDisplayName.Checked = !isWorking;

            if(!isWorking)
            {
                fullList = GetListEntitiesFromListView();
            }
        }

        private void tsmiLoadEntitiesFromSolution_Click(object sender, EventArgs e)
        {
            ExecuteMethod(LoadEntities, true);
        }

        private void tssbLoadEntities_ButtonClick(object sender, EventArgs e)
        {
            ExecuteMethod(LoadEntities, false);
        }

        #endregion Methods

        private void BtnBrowseFilePathClick(object sender, EventArgs e)
        {
            var sfDialog = new SaveFileDialog
            {
                Filter =
                                       cbbOutputType.SelectedIndex == 0
                                           ? "Excel workbook|*.xlsx"
                                           : "Word Document|*.docx",
                Title = "Select a location for the file generated"
            };

            if (sfDialog.ShowDialog(this) == DialogResult.OK)
            {
                txtOutputFilePath.Text = sfDialog.FileName;
            }
        }

        private void btnEdit_Click(object sender, EventArgs e)
        {
            var pSelector = new PublisherSelector(Service, txtPrefixes.Text);
            if (pSelector.ShowDialog(this) == DialogResult.OK)
            {
                txtPrefixes.Text = string.Join(";", pSelector.SelectedPrefixes);
            }
        }

        private void CbbOutputTypeSelectedIndexChanged(object sender, EventArgs e)
        {
            txtOutputFilePath.Text = string.Empty;
        }

        private void CbbSelectionTypeSelectedIndexChanged(object sender, EventArgs e)
        {
            if (settings == null || cbbSelectionType.SelectedIndex == (int)settings.AttributesSelection)
                return;

            if (settings.EntitiesToProceed.Count != 0)
            {
                if (MessageBox.Show(this,
                                    "If you change selection type, all previous selected attributes or forms will be cleared from current selection. \r\n\r\nDo you want to continue?",
                                    "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
                {
                    cbbSelectionType.SelectedIndex = (int)settings.AttributesSelection;
                    return;
                }
            }

            foreach (var item in settings.EntitiesToProceed)
            {
                item.Attributes.Clear();
                item.Forms.Clear();
            }

            DisplaySubSelectionComponents();
        }

        private void chkFilterByPrefix_CheckedChanged(object sender, EventArgs e)
        {
            btnEdit.Enabled = chkFilterByPrefix.Checked;
            txtPrefixes.Enabled = chkFilterByPrefix.Checked;
        }

        private void chkSelectAll_CheckedChanged(object sender, EventArgs e)
        {
            foreach (ListViewItem item in lvEntities.Items)
            {
                item.Checked = chkSelectAll.Checked;
            }
        }

        private void DisplaySubSelectionComponents()
        {
            lvAttributes.Items.Clear();
            lvForms.Items.Clear();

            settings.AttributesSelection = (AttributeSelectionOption)cbbSelectionType.SelectedIndex;

            switch (cbbSelectionType.SelectedIndex)
            {
                case (int)AttributeSelectionOption.AllAttributes:
                case (int)AttributeSelectionOption.AttributesOptionSet:
                    {
                        lvAttributes.Visible = false;
                        lvForms.Visible = false;
                        lblSubSelect.Visible = false;
                    }
                    break;

                case (int)AttributeSelectionOption.AttributesOnForm:
                case (int)AttributeSelectionOption.AttributesNotOnForm:
                    {
                        lvAttributes.Visible = false;
                        lvForms.Visible = true;
                        lblSubSelect.Visible = true;
                        lblSubSelect.Text = "Forms";
                    }
                    break;

                case (int)AttributeSelectionOption.AttributeManualySelected:
                    {
                        lvAttributes.Visible = true;
                        lvForms.Visible = false;
                        lblSubSelect.Visible = true;
                        lblSubSelect.Text = "Attributes";
                        LvEntitiesSelectedIndexChanged(null, null);
                    }
                    break;
            }
        }

        private void ListViewsColumnClick(object sender, ColumnClickEventArgs e)
        {
            var lv = (ListView)sender;

            lv.Sorting = lv.Sorting == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            lv.ListViewItemSorter = new ListViewItemComparer(e.Column, lv.Sorting);
            lv.Sort();
        }

        private void LvAttributesItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (lvEntities.SelectedItems.Count == 0)
                return;

            var currentEntityName = lvEntities.SelectedItems[0].Tag.ToString();

            var currentEntity = settings.EntitiesToProceed.FirstOrDefault(x => x.Name == currentEntityName);
            if (currentEntity == null)
            {
                lvEntities.SelectedItems[0].Checked = true;
                currentEntity = settings.EntitiesToProceed.FirstOrDefault(x => x.Name == currentEntityName);
            }

            if (e.Item.Checked)
            {
                currentEntity.Attributes.Add(e.Item.Tag.ToString());
            }
            else
            {
                var item = currentEntity.Attributes.FirstOrDefault(x => x == e.Item.Tag.ToString());
                if (item != null)
                {
                    currentEntity.Attributes.Remove(item);
                }
            }
        }

        private void LvEntitiesItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (e.Item.Checked)
            {
                if (settings.EntitiesToProceed.Any(x => x.Name == e.Item.Tag.ToString()))
                    return;

                settings.EntitiesToProceed.Add(new EntityItem
                {
                    Attributes = new List<string>(),
                    Name = e.Item.Tag.ToString(),
                    Forms = new List<Guid>()
                });
            }
            else
            {
                var item = settings.EntitiesToProceed.FirstOrDefault(x => x.Name == e.Item.Tag.ToString());
                if (item != null)
                {
                    settings.EntitiesToProceed.Remove(item);
                }
            }
        }

        private void LvEntitiesSelectedIndexChanged(object sender, EventArgs e)
        {
            if (lvEntities.SelectedItems.Count == 0)
            {
                return;
            }

            var currentEntity = settings.EntitiesToProceed.FirstOrDefault(x => x.Name == lvEntities.SelectedItems[0].Tag.ToString());
            if (currentEntity == null)
            {
                lvEntities.SelectedItems[0].Checked = true;
            }

            if (cbbSelectionType.SelectedIndex == (int)AttributeSelectionOption.AttributeManualySelected)
            {
                lvAttributes.Items.Clear();

                var entityName = lvEntities.SelectedItems[0].Tag.ToString();
                SetWorkingState(true);

                WorkAsync(new WorkAsyncInfo
                {
                    Message = "Retrieving attributes...",
                    AsyncArgument = entityName,
                    Work = (bw, evt) =>
                    {
                        var entityLogicalName = evt.Argument.ToString();

                        var request = new RetrieveEntityRequest { EntityFilters = EntityFilters.Attributes, LogicalName = entityLogicalName };
                        var response = (RetrieveEntityResponse)Service.Execute(request);

                        evt.Result = response.EntityMetadata;
                    },
                    PostWorkCallBack = evt =>
                    {
                        var currentEntityMd = settings.EntitiesToProceed.FirstOrDefault(x => x.Name == lvEntities.SelectedItems[0].Tag.ToString());

                        foreach (var amd in ((EntityMetadata)evt.Result).Attributes)
                        {
                            var displayName = amd.DisplayName != null && amd.DisplayName.UserLocalizedLabel != null
                                                  ? amd.DisplayName.UserLocalizedLabel.Label
                                                  : "N/A";

                            var item = new ListViewItem(displayName);
                            item.SubItems.Add(amd.LogicalName);
                            item.Tag = amd.LogicalName;

                            if (currentEntityMd != null && currentEntityMd.Attributes.Contains(amd.LogicalName))
                            {
                                item.Checked = true;
                            }

                            lvAttributes.Items.Add(item);
                        }

                        SetWorkingState(false);
                    }
                });
            }
            else if (cbbSelectionType.SelectedIndex == (int)AttributeSelectionOption.AttributesOnForm
                || cbbSelectionType.SelectedIndex == (int)AttributeSelectionOption.AttributesNotOnForm)
            {
                lvForms.Items.Clear();

                var entityName = lvEntities.SelectedItems[0].Tag.ToString();
                SetWorkingState(true);

                var theEntity = settings.EntitiesToProceed.First(x => x.Name == lvEntities.SelectedItems[0].Tag.ToString());
                theEntity.FormsDefinitions.Clear();

                WorkAsync(new WorkAsyncInfo
                {
                    Message = "Retrieving forms...",
                    AsyncArgument = entityName,
                    Work = (bw, evt) =>
                    {
                        var qe = new QueryExpression("systemform")
                        {
                            ColumnSet = new ColumnSet(true),
                            Criteria = new FilterExpression
                            {
                                Conditions =
                                {
                                    new ConditionExpression("objecttypecode", ConditionOperator.Equal,
                                        evt.Argument.ToString()),
                                    new ConditionExpression("type", ConditionOperator.In, new[] {2, 7})
                                }
                            }
                        };

                        evt.Result = Service.RetrieveMultiple(qe).Entities;
                    },
                    PostWorkCallBack = evt =>
                    {
                        var currentEntityMd = settings.EntitiesToProceed.FirstOrDefault(x => x.Name == lvEntities.SelectedItems[0].Tag.ToString());

                        foreach (var form in (DataCollection<Entity>)evt.Result)
                        {
                            currentEntityMd.FormsDefinitions.Add(form);

                            var item = new ListViewItem(form.GetAttributeValue<string>("name")) { Tag = form };

                            if (currentEntityMd != null && currentEntityMd.Forms.Contains(form.Id))
                            {
                                item.Checked = true;
                            }

                            lvForms.Items.Add(item);
                        }

                        SetWorkingState(false);
                    }
                });
            }
        }

        private void LvFormsItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (lvEntities.SelectedItems.Count == 0 || lvForms.CheckedItems.Count == 0)
                return;

            var currentEntityName = lvEntities.SelectedItems[0].Tag.ToString();

            var currentEntity = settings.EntitiesToProceed.FirstOrDefault(x => x.Name == currentEntityName);
            if (currentEntity == null)
            {
                lvEntities.SelectedItems[0].Checked = true;
                currentEntity = settings.EntitiesToProceed.FirstOrDefault(x => x.Name == currentEntityName);
            }

            var form = (Entity)e.Item.Tag;

            if (e.Item.Checked)
            {
                if (!currentEntity.Forms.Contains(form.Id))
                    currentEntity.Forms.Add(form.Id);
            }
            else
            {
                if (currentEntity.Forms.Contains(form.Id) && loadSettingsFlag == false)
                {
                    currentEntity.Forms.Remove(form.Id);
                }
            }
        }

        private void TsbGenerateClick(object sender, EventArgs e)
        {
            if (settings.EntitiesToProceed.Count == 0)
            {
                MessageBox.Show(this, "No entity has been selected", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (txtOutputFilePath.Text.Length == 0)
            {
                MessageBox.Show(this, "Please select a destination file", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            settings.AddAuditInformation = chkAddAudit.Checked;
            settings.AddEntitiesSummary = chkDisplayEntityList.Checked;
            settings.AddFieldSecureInformation = chkAddFls.Checked;
            settings.AddRequiredLevelInformation = chkAddRequiredLevel.Checked;
            settings.AddValidForAdvancedFind = chkAddValidForAf.Checked;
            settings.AddFormLocation = chkAddFormLocation.Checked;
            settings.GenerateOnlyOneTable = chkOneSheet.Checked;

            settings.DisplayNamesLangugageCode = ((LanguageCode)cbbLcid.SelectedItem).Lcid;
            settings.FilePath = txtOutputFilePath.Text;
            settings.OutputDocumentType = cbbOutputType.SelectedIndex == 0 ? Output.Excel : Output.Word;
            settings.AttributesSelection = (AttributeSelectionOption)cbbSelectionType.SelectedIndex;
            settings.IncludeOnlyAttributesOnForms = cbbSelectionType.SelectedIndex == (int)AttributeSelectionOption.AttributesOnForm;
            settings.Prefixes = chkFilterByPrefix.Checked ? txtPrefixes.Text.Split(';').ToList() : new List<string>();

            SetWorkingState(true);

            WorkAsync(new WorkAsyncInfo
            {
                Message = "",
                AsyncArgument = null,
                Work = (bw, evt) =>
                {
                    // If we need attribute location but miss at least one
                    // form definition for entity, then we retrieve forms
                    // again
                    if ((settings.AttributesSelection == AttributeSelectionOption.AllAttributes
                         || settings.AttributesSelection == AttributeSelectionOption.AttributesUnmanaged
                        || settings.AttributesSelection == AttributeSelectionOption.AttributeManualySelected
                        || settings.AttributesSelection == AttributeSelectionOption.AttributesOptionSet)
                        && settings.AddFormLocation
                        && settings.EntitiesToProceed.Any(entity => entity.FormsDefinitions.Count == 0))
                    {
                        bw.ReportProgress(0, "Loading forms definitions...");

                        var qba = new QueryExpression("systemform")
                        {
                            ColumnSet = new ColumnSet(true),
                            Criteria = new FilterExpression
                            {
                                Conditions =
                                {
                                    new ConditionExpression("objecttypecode", ConditionOperator.In,
                                        settings.EntitiesToProceed.Select(entity => entity.Name).ToArray()),
                                    new ConditionExpression("type", ConditionOperator.In, new[] {2, 7})
                                }
                            }
                        };

                        foreach (var form in Service.RetrieveMultiple(qba).Entities)
                        {
                            settings.EntitiesToProceed.First(entity => entity.Name == form.GetAttributeValue<string>("objecttypecode")).FormsDefinitions.Add(form);
                        }
                    }

                    IDocument docGenerator;

                    if (settings.OutputDocumentType == Output.Excel)
                    {
                        docGenerator = new ExcelDocument();
                        docGenerator.Worker = bw;
                        docGenerator.Settings = settings;
                        docGenerator.Generate(Service);
                    }
                    else
                    {
                        // Depecrated
                        docGenerator = new WordDocumentOpenXml(); //WordDocumentDocX();
                        docGenerator.Worker = bw;
                        docGenerator.Settings = settings;
                        docGenerator.Generate(Service);
                    }
                },
                PostWorkCallBack = evt =>
                {
                    SetWorkingState(false);

                    if (evt.Error != null)
                    {
                        MessageBox.Show(this, "An error occured while generating document: " + evt.Error.ToString(), "Error",
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        if (DialogResult.Yes == MessageBox.Show(this, "Do you want to open generated document?", "Question", MessageBoxButtons.YesNo, MessageBoxIcon.Question))
                        {
                            Process.Start(settings.FilePath);
                        }
                    }
                },
                ProgressChanged = evt => { SetWorkingMessage(string.Format("{0}%\r\n{1}", evt.ProgressPercentage, evt.UserState)); }
            });
        }

        #region Settings

        private void LoadSettingsToolStripMenuItemClick(object sender, EventArgs e)
        {
            loadSettingsFlag = true;
            settings = GenerationSettings.CreateFromFile();

            cbbOutputType.SelectedIndex = settings.OutputDocumentType == Output.Excel ? 0 : 1;
            cbbSelectionType.SelectedIndex = (int)settings.AttributesSelection;

            DisplaySubSelectionComponents();

            txtOutputFilePath.Text = settings.FilePath;
            chkAddAudit.Checked = settings.AddAuditInformation;
            chkAddFls.Checked = settings.AddFieldSecureInformation;
            chkAddFormLocation.Checked = settings.AddFormLocation;
            chkAddRequiredLevel.Checked = settings.AddRequiredLevelInformation;
            chkAddValidForAf.Checked = settings.AddValidForAdvancedFind;
            chkDisplayEntityList.Checked = settings.AddEntitiesSummary;
            chkOneSheet.Checked = settings.GenerateOnlyOneTable;

            foreach (ListViewItem item in lvEntities.Items)
            {
                var eItem = settings.EntitiesToProceed.FirstOrDefault(x => x.Name == item.Tag.ToString());
                item.Checked = eItem != null;
            }

            loadSettingsFlag = false;
        }

        private void SaveCurrentSettingsToolStripMenuItemClick(object sender, EventArgs e)
        {
            settings.OutputDocumentType = (Output)cbbOutputType.SelectedIndex;
            settings.AttributesSelection = (AttributeSelectionOption)cbbSelectionType.SelectedIndex;
            settings.FilePath = txtOutputFilePath.Text;
            settings.AddAuditInformation = chkAddAudit.Checked;
            settings.AddFieldSecureInformation = chkAddFls.Checked;
            settings.AddFormLocation = chkAddFormLocation.Checked;
            settings.AddRequiredLevelInformation = chkAddRequiredLevel.Checked;
            settings.AddValidForAdvancedFind = chkAddValidForAf.Checked;
            settings.AddEntitiesSummary = chkDisplayEntityList.Checked;
            settings.GenerateOnlyOneTable = chkOneSheet.Checked;

            settings.SaveToFile();
        }

        #endregion Settings

        #region IGithub implementation

        public string RepositoryName => "MsCrmTools.MetadataDocumentGenerator";
        public string UserName => "MscrmTools";

        #endregion IGithub implementation

        #region Filter by Entity name

        private void txtSearchEntity_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                FilterEntity();
            }
        }

        private List<Entities> fullList;

        private void FilterEntity()
        {
            string filterText = this.txtSearchEntity.Text.Trim().ToUpper();
            if (filterText.Length == 0)
            {
                //Clear filters show all entities
                FillListEntities(fullList);
            }
            else
            {
                bool searchBySchemaName = rbSchemaName.Checked;
                List<Entities> filteredEntities = null;
                if (searchBySchemaName)
                {
                    filteredEntities = this.fullList.Where(x => x.SchemaName.ToUpper().StartsWith(filterText)).ToList();
                }
                else
                {
                    filteredEntities = this.fullList.Where(x => x.DisplayName.ToUpper().StartsWith(filterText)).ToList();
                }
                FillListEntities(filteredEntities.Count == 0 ? fullList : filteredEntities);
            }
        }

        private void FillListEntities(List<Entities> items)
        {
            lvEntities.Items.Clear();
            lvEntities.BeginUpdate();
            foreach (var item in items)
            {
                lvEntities.Items.Add(new ListViewItem
                {
                    Text = item.DisplayName,
                    SubItems =
                    {
                        item.SchemaName
                    },
                    Tag = item.SchemaName
                });
            }
            lvEntities.EndUpdate();
        }

        private List<Entities> GetListEntitiesFromListView()
        {
            List<Entities> listEntities = new List<Entities>();
            foreach (var item in lvEntities.Items)
            {
                var itemDetail = (ListViewItem)item;
                listEntities.Add(new Entities(){ DisplayName = itemDetail.Text, SchemaName = itemDetail.Tag.ToString() });
            }
            return listEntities;
        }

        public class Entities
        {
            public string DisplayName { get; set; }
            public string SchemaName { get; set; }
        }

        #endregion Filter by Entity name
    }
}