using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using interop.CimBaseAPI;
using interop.CimMdlrAPI;
using interop.CimServicesAPI;
using stdole;
// CimBaseAPI and CimMdlrAPI declare overlapping type names. Pin each shared
// type to CimBaseAPI so unqualified uses below compile without CS0104.
// See CLAUDE.md at the project root before adding new files that touch both.
using ICimEntity = interop.CimBaseAPI.ICimEntity;
using ICimEntityList = interop.CimBaseAPI.ICimEntityList;
using ICimDocument = interop.CimBaseAPI.ICimDocument;
using EntityEnumType = interop.CimBaseAPI.EntityEnumType;
using IEntityFilter = interop.CimBaseAPI.IEntityFilter;
using IEntityQuery = interop.CimBaseAPI.IEntityQuery;
using EFilterEnumType = interop.CimBaseAPI.EFilterEnumType;
using InteractionType = interop.CimBaseAPI.InteractionType;
using IInteraction = interop.CimBaseAPI.IInteraction;

namespace ApiName.Helpers
{
    // Thin wrapper around Cimatron's FeatureGuide interaction. Construct one
    // per active document, add FG_Stage instances for each pick step, then
    // call Activate(). Wire OnApply / OnOk / OnCancel / OnPreview to your
    // command logic.
    public class FeatureGuide
    {
        private interop.CimServicesAPI.FeatureGuide mFG;
        private readonly List<FG_Stage> mStages = new List<FG_Stage>();

        public static Bitmap GetBitmapFromResource(string resourceName)
        {
            Bitmap bitmap = null;
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        bitmap = new Bitmap(stream);
                    }
                    else
                    {
                        Logger.LogError($"Resource '{resourceName}' not found.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Failed to load bitmap from resource '{resourceName}'.");
            }
            return bitmap;
        }

        public List<FG_Stage> GetStagesList() => mStages;

        public FeatureGuide(interop.CimBaseAPI.ICimDocument doc)
        {
            var sink = (interop.CimBaseAPI.IInteractionSink)doc;
            mFG = (interop.CimServicesAPI.FeatureGuide)sink.CreateInteraction(InteractionType.cmFeatureGuide);
            mFG.OnApply += Apply;
            mFG.OnCancel += Cancel;
            mFG.OnOk += Ok;
            mFG.OnPreview += Preview;
            mFG.OnStagePressed += StagePressed;
            mFG.OnStageReleased += StageReleased;
            mFG.OnPop += Pop;
        }

        ~FeatureGuide()
        {
            mFG = null;
            GC.Collect();
        }

        public void SetTitle(string title) => mFG.SetTitle(title);
        public void Activate() => mFG.Activate();
        public void EnableStage(short iStageIndex, int iEnable) => mFG.EnableStage(iStageIndex, iEnable);
        public void SetInitStage() => mFG.SetInitStage();

        public void AddStage(FG_Stage stage)
        {
            mStages.Add(stage);
            stage.SetFeatureGuid(mFG);
            mFG.AddStage(stage);
        }

        public void RemoveStage(FG_Stage stage)
        {
            mStages.Remove(stage);
            mFG.RemoveStage(stage);
        }

        public void SetBitmap(Bitmap bitmap) =>
            mFG.SetBitmap((IPicture)OleImageConverter.ImageToIpicture(bitmap));

        public void ShowButton(FeatureGuideButtons button, short show) =>
            mFG.ShowButton(button, show);

        public event EventHandler OnApply;
        public void Apply() => OnApply?.Invoke(this, EventArgs.Empty);

        public event EventHandler OnPreview;
        public void Preview() => OnPreview?.Invoke(this, EventArgs.Empty);

        public event EventHandler OnCancel;
        public void Cancel() => OnCancel?.Invoke(this, EventArgs.Empty);

        public event EventHandler OnOk;
        public void Ok() => OnOk?.Invoke(this, EventArgs.Empty);

        public event EventHandler OnStagePressed;
        public void StagePressed(short iStageIndex) => OnStagePressed?.Invoke(this, EventArgs.Empty);

        public event EventHandler OnStageReleased;
        public void StageReleased(short iStageIndex) => OnStageReleased?.Invoke(this, EventArgs.Empty);

        public event EventHandler OnPop;
        public void Pop() => OnPop?.Invoke(this, EventArgs.Empty);

        public event EventHandler OnDone;
        public void Done(object iData) => OnDone?.Invoke(this, EventArgs.Empty);

        public void OnEvent(SPFigure spFigure, ISPControl spControl, T_SPEventType eventType, object value)
        {
        }
    }

    // A single pick stage in the FeatureGuide. Pass the document + the
    // EntityEnumType list it should accept (e.g. cmEdge, cmFace). The stage
    // tracks picked entities in FeatureGuidEntityList and applies the filter
    // when the stage is pushed.
    public class FG_Stage : FeatureGuideStage,
        _IFeatureGuideStageEvents_Event,
        IFeatureGuideStageEventsDelegator,
        Tool,
        IToolEvents,
        IPickToolEvents
    {
        protected interop.CimServicesAPI.FeatureGuide mFG;
        private short mIndex = 1;
        private short mOptional = 0;

        protected IPickTool m_PickToolHelper;
        public IEntityFilter m_EnttFilter;
        protected long m_EntityCnt = int.MaxValue;
        protected IFilterPoint m_PointFilter;
        protected ICimDocument m_aDoc;

        public FeatureGuidEntityList FeatureGuidEntityList { get; set; }

        public FG_Stage(ICimDocument aDoc, List<EntityEnumType> filterTypes)
        {
            m_aDoc = aDoc;
            FeatureGuidEntityList = new FeatureGuidEntityList();
            CreateFilters(filterTypes);
        }

        ~FG_Stage()
        {
            mFG = null;
            GC.Collect();
        }

        #region IFeatureGuideStage

        public void SetFeatureGuid(interop.CimServicesAPI.FeatureGuide iFG) => mFG = iFG;

        public void SetIndex(short index) => mIndex = index;

        short IFeatureGuideStage.Index
        {
            get => mIndex;
            set => mIndex = value;
        }

        public void SetOptional(short optional) => mOptional = optional;

        int IFeatureGuideStage.Optional
        {
            get => mOptional;
            set => mOptional = short.Parse(value.ToString());
        }

        protected string toolTip = "Required";

        public void SetToolTip(string toolTip) => this.toolTip = toolTip;

        string IFeatureGuideStage.Tooltip
        {
            get => toolTip;
            set => throw new NotImplementedException();
        }

        protected Bitmap bitmap = null;

        public void SetBitmap(Bitmap bitmap) => this.bitmap = bitmap;

        IPicture IFeatureGuideStage.Bitmap
        {
            get => (IPicture)OleImageConverter.ImageToIpicture(bitmap);
            set => throw new NotImplementedException();
        }

        public IPicture OnSetCursor(int iXPos, int iYPos) => null;

        #endregion

        #region _IFeatureGuideStageEvents_Event

        public event _IFeatureGuideStageEvents_OnPressedEventHandler OnPressed;
        public event _IFeatureGuideStageEvents_OnReleasedEventHandler OnReleased;

        #endregion

        public void ClearSelection()
        {
            var selection = (ISelection)m_aDoc;
            selection.Selection = (interop.CimMdlrAPI.ICimEntityList)(CimEntityList)
                Activator.CreateInstance(Marshal.GetTypeFromCLSID(new Guid("3A63FCB0-2CE0-11D4-B7FF-00105ACCAC8E")));
        }

        public void SetSelection(ICimEntityList objects)
        {
            var selection = (ISelection)m_aDoc;
            selection.Selection = (interop.CimMdlrAPI.ICimEntityList)objects;
        }

        #region IFeatureGuideStageEventsDelegator

        void IFeatureGuideStageEventsDelegator.OnPressed() => SetSelection(FeatureGuidEntityList.GetEntities());
        void IFeatureGuideStageEventsDelegator.OnReleased() => ClearSelection();

        #endregion

        private IEntityFilter Createfilter(List<EntityEnumType> filters)
        {
            var query = (IEntityQuery)((IModelContainer)m_aDoc).Model;
            var aFilter = (FilterType)query.CreateFilter(EFilterEnumType.cmFilterEntityType);
            foreach (var t in filters)
            {
                aFilter.Add((interop.CimBaseAPI.EntityEnumType)t);
            }
            return (IEntityFilter)aFilter;
        }

        private void CreateFilters(List<EntityEnumType> filters)
        {
            m_EnttFilter = Createfilter(filters);
        }

        #region _PickTool_Members

        public void OnToolPushed(object iToolHelper)
        {
            Logger.LogInfo("Tool Pushed, Activating Filter...");
            ActivateFilter(iToolHelper);
        }

        public void ActivateFilter(object iToolHelper)
        {
            if (iToolHelper == null)
            {
                MessageBox.Show("iToolHelper is null");
            }
            else
            {
                m_PickToolHelper = (IPickTool)iToolHelper;
            }

            if (m_EnttFilter != null)
            {
                // IPickTool.SetFilter wants the CimServicesAPI variant, which is a
                // third declaration of IEntityFilter distinct from the CimBaseAPI alias.
                m_PickToolHelper.SetFilter((interop.CimServicesAPI.IEntityFilter)m_EnttFilter, 0);
            }

            if (m_PointFilter != null)
            {
                m_PickToolHelper.SetFilter((interop.CimServicesAPI.IEntityFilter)m_PointFilter, 0);
            }
        }

        public void OnToolPoped() { }

        public int ToolLevel { get; set; }

        public void OnMouseEvent(MouseEventType iType, int iXPos, int iYPos) { }

        public void OnKeyboardEvent(int iChar, int iRepCnt, int iFlags) { }

        public int OnBlockPop() => 1;

        public void SetSelectionMaxSize(long count) => m_EntityCnt = count;

        public void OnEntityDraged(ICimEntity iEntity, int iXPos, int iYPos) { }
        public void OnEntityHighlighted(ICimEntity iEntity, int iXPos, int iYPos) { }
        public virtual void OnEntityPressed(ICimEntity iEntity, int iXPos, int iYPos) { }
        public void OnEntityReleased(ICimEntity iEntity, int iXPos, int iYPos) { }

        public class EntityPickedValidateEventArgs : EventArgs
        {
            public ICimEntity Entity;
            public bool IsValid { get; set; } = true;
            public EntityPickedValidateEventArgs(ICimEntity entity) { Entity = entity; }
        }

        public event EventHandler<EntityPickedValidateEventArgs> EntityPickedValidate;

        public void OnEntityPicked(ICimEntity iEntity)
        {
            if (EntityPickedValidate != null)
            {
                var args = new EntityPickedValidateEventArgs(iEntity);
                EntityPickedValidate(this, args);
                if (!args.IsValid)
                {
                    SetSelection(FeatureGuidEntityList.GetEntities());
                    return;
                }
            }

            if (iEntity == null) return;

            if (FeatureGuidEntityList.ContainsKey(iEntity.ID))
            {
                FeatureGuidEntityList.RemoveEntity(iEntity.ID);
                return;
            }

            if (m_EntityCnt == 1)
            {
                FeatureGuidEntityList.ClearEntities();
                FeatureGuidEntityList.AddEntity(iEntity.ID, iEntity);
                SetSelection(FeatureGuidEntityList.GetEntities());
            }
            else if (FeatureGuidEntityList.Count < m_EntityCnt)
            {
                FeatureGuidEntityList.AddEntity(iEntity.ID, iEntity);
            }
            else
            {
                MessageBox.Show($"You can select a maximum of {m_EntityCnt} entities.");
            }
        }

        public void OnNoEntityPicked(int iXPos, int iYPos) { }

        public void OnClearSelection() => FeatureGuidEntityList.ClearEntities();

        public void FigureChange(InteractionType iType, IInteraction oInteraction) { }

        #endregion
    }

    internal class OleImageConverter : AxHost
    {
        public OleImageConverter() : base("59EE46BA-677D-4d20-BF10-8D8067CB8B33") { }

        public static IPictureDisp ImageToIpicture(Image image) =>
            (IPictureDisp)GetIPictureDispFromPicture(image);

        public static Image IPictureToImage(StdPicture picture) =>
            GetPictureFromIPicture(picture);
    }

    public class FeatureGuidEntityList
    {
        public IAssemblyModel aAssemblyModel { get; set; }
        private readonly Dictionary<string, ICimEntity> mKeyEntityPair = new Dictionary<string, ICimEntity>();

        public int Count => mKeyEntityPair.Count;

        public void AddEntity(int key, ICimEntity entity) =>
            mKeyEntityPair.Add(key.ToString(), entity);

        private string GetDefaultKey(ICimEntity entity)
        {
            try
            {
                if (aAssemblyModel != null)
                {
                    var instance = aAssemblyModel.InstanceByAssemblyEnt[(interop.CimMdlrAPI.ICimEntity)entity];
                    var rootTitle = instance.AssRootInstance.Model.Title;
                    var key = $":{instance.ID}";
                    var parent = instance.AssParentInstance;
                    var maxIterations = 100;
                    while (parent.Model.Title != rootTitle)
                    {
                        key = $":{parent.ID}{key}";
                        parent = parent.AssParentInstance;
                        if (maxIterations-- <= 0)
                        {
                            Logger.LogWarning("Max iterations reached while building instance key.");
                            break;
                        }
                    }
                    return key;
                }
                Logger.LogWarning("Assembly Model is null, cannot get instance key.");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Failed to get Instance Key");
            }
            return $"{entity.Model.Title}::{entity.ID}";
        }

        public string AddEntity(ICimEntity entity)
        {
            var key = GetDefaultKey(entity);
            mKeyEntityPair.Add(key, entity);
            return key;
        }

        public ICimEntityList GetEntities()
        {
            var list = (ICimEntityList)new CimEntityList();
            foreach (var kvp in mKeyEntityPair) list.Add(kvp.Value);
            return list;
        }

        public List<ICimEntity> GetEntityList()
        {
            var list = new List<ICimEntity>();
            foreach (var kvp in mKeyEntityPair) list.Add(kvp.Value);
            return list;
        }

        public bool ContainsKey(int key) => mKeyEntityPair.ContainsKey(key.ToString());
        public bool ContainsEntity(ICimEntity entity) => mKeyEntityPair.ContainsKey(GetDefaultKey(entity));

        public string RemoveEntity(ICimEntity entity)
        {
            var key = GetDefaultKey(entity);
            mKeyEntityPair.Remove(key);
            return key;
        }

        public void RemoveEntity(int key) => mKeyEntityPair.Remove(key.ToString());

        public void ClearEntities() => mKeyEntityPair.Clear();
    }
}
