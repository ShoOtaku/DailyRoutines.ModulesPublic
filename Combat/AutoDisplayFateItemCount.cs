using System.Numerics;
using DailyRoutines.Abstracts;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;
using KamiToolKit.Overlay;
using Lumina.Excel.Sheets;

namespace DailyRoutines.ModulesPublic;

public unsafe class AutoDisplayFateItemCount : DailyModuleBase
{
    public override ModuleInfo Info { get; } = new()
    {
        Title           = GetLoc("AutoDisplayFateItemCountTitle"),
        Description     = GetLoc("AutoDisplayFateItemCountDescription"),
        Category        = ModuleCategories.Combat,
        PreviewImageURL = ["https://gh.atmoomen.top/raw.githubusercontent.com/AtmoOmen/StaticAssets/main/DailyRoutines/image/AutoDisplayFateItemCount-UI.png"]
    };

    public override ModulePermission Permission { get; } = new() { AllDefaultEnabled = true };
    
    private static OverlayController? Controller;
    
    protected override void Init()
    {
        Controller ??= new();
        Controller.CreateNode(() => new FateInfoNode());
    }

    protected override void Uninit()
    {
        Controller?.Dispose();
        Controller = null;
    }

    private class FateInfoNode : OverlayNode
    {
        public override OverlayLayer OverlayLayer     => OverlayLayer.Foreground;
        public override bool         HideWithNativeUI => false;

        private GridNode TableNode { get; }
        
        private HorizontalListNode LeftHeaderNode { get; }
        private IconImageNode      IconLeftNode   { get; }
        private TextNode           NameLeftNode   { get; }

        private TextNode      HoldLabelNode   { get; }
        private TextNode      HoldCountNode   { get; }
        private TextNode      HandInLabelNode { get; }
        private TextNode      HandInCountNode { get; }

        public FateInfoNode()
        {
            Scale = new(1.5f);
            Size  = new(200, 76);
            
            LeftHeaderNode = new HorizontalListNode
            {
                Size      = new(200, 36),
                IsVisible = false
            };

            IconLeftNode = new()
            {
                Size       = new(32),
                IconId     = 60498,
                FitTexture = true,
                IsVisible  = true
            };
            LeftHeaderNode.AddNode(IconLeftNode);

            LeftHeaderNode.AddDummy(3f);

            NameLeftNode = new()
            {
                Size             = new(160, 64),
                SeString         = "测试物品",
                FontSize         = 20,
                Position         = new(2),
                TextFlags        = TextFlags.Edge,
                AlignmentType    = AlignmentType.TopLeft,
                TextOutlineColor = ColorHelper.GetColor(30),
            };
            LeftHeaderNode.AddNode(NameLeftNode);
            
            LeftHeaderNode.AttachNode(this);
            

            TableNode = new()
            {
                Position = new(0, 40),
                Size     = new Vector2(100, 28) * 2,
                GridSize = new(2, 2),
            };

            HoldLabelNode = new()
            {
                SeString         = GetLoc("AutoDisplayFateItemCount-HoldCount"),
                FontSize         = 14,
                TextFlags        = TextFlags.Edge,
                TextOutlineColor = ColorHelper.GetColor(37),
            };
            HoldLabelNode.AttachNode(TableNode[0, 0]);
            
            HoldCountNode = new()
            {
                SeString         = "0",
                FontSize         = 18,
                TextFlags        = TextFlags.Edge,
                FontType         = FontType.Miedinger,
                TextOutlineColor = ColorHelper.GetColor(30),
            };
            HoldCountNode.AttachNode(TableNode[0, 1]);
            
            HandInLabelNode = new()
            {
                SeString         = GetLoc("AutoDisplayFateItemCount-HandInCount"),
                FontSize         = 14,
                TextFlags        = TextFlags.Edge,
                TextOutlineColor = ColorHelper.GetColor(37),
            };
            HandInLabelNode.AttachNode(TableNode[1, 0]);
            
            HandInCountNode = new()
            {
                SeString         = "0",
                FontSize         = 18,
                TextFlags        = TextFlags.Edge,
                FontType         = FontType.Miedinger,
                TextOutlineColor = ColorHelper.GetColor(30),
            };
            HandInCountNode.AttachNode(TableNode[1, 1]);
            
            TableNode.AttachNode(this);
        }
        
        public override void Update()
        {
            base.Update();

            var currentFate = FateManager.Instance()->CurrentFate;
            if (currentFate == null                                                  ||
                !LuminaGetter.TryGetRow<Fate>(currentFate->FateId, out var fateData) ||
                fateData.EventItem.RowId      == 0                                   ||
                fateData.EventItem.Value.Icon == 0                                   ||
                !IsAddonAndNodesReady(ToDoList))
            {
                IsVisible = false;
                return;
            }

            IsVisible = true;

            AtkResNode*       nodeItemCount   = null;
            AtkComponentNode* nodeDescription = null;
            AtkComponentNode* nodeProgressBar = null;
            for (var i = 0; i < ToDoList->UldManager.NodeListCount; i++)
            {
                var node = ToDoList->UldManager.NodeList[i];
                if (node == null) continue;

                if (node->NodeId == 23001)
                    nodeItemCount = node;
                if (node->NodeId == 63101)
                    nodeDescription = (AtkComponentNode*)node;
                if (node->NodeId == 113001)
                    nodeProgressBar = (AtkComponentNode*)node;
            }
            
            if (nodeItemCount == null || nodeDescription == null || nodeProgressBar == null) return;

            var progressBarState = NodeState.Get(nodeProgressBar->Component->UldManager.SearchNodeById(4));

            var nodeState0 = NodeState.Get((AtkResNode*)nodeProgressBar);
            var nodeState1 = NodeState.Get((AtkResNode*)nodeDescription);
            var nodeState2 = NodeState.Get(nodeItemCount);
            Position = progressBarState.Position                                                 + 
                       new Vector2(0, nodeState0.Size.Y + nodeState1.Size.Y + nodeState2.Size.Y) +
                       new Vector2(0, 12);

            UpdateFateItemHeader(fateData.EventItem.Value);
            HoldCountNode.SetNumber((int)LocalPlayerState.GetItemCount(fateData.EventItem.RowId));
            HandInCountNode.SetNumber(currentFate->HandInCount);
        }

        private void UpdateFateItemHeader(EventItem item)
        {
            LeftHeaderNode.IsVisible  = true;
            
            IconLeftNode.IconId   = item.Icon;
            NameLeftNode.SeString = $"{item.Singular}";
            while (NameLeftNode.FontSize > 1 && NameLeftNode.GetTextDrawSize(NameLeftNode.SeString).X > NameLeftNode.Size.X)
                NameLeftNode.FontSize--;
        }
    }
}
