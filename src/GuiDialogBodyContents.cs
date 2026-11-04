using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace OfflineBodies
{
    /// <summary>Chest-style window showing what a sleeping body is carrying.</summary>
    public class GuiDialogBodyContents : GuiDialogGeneric
    {
        private readonly EntitySleepingBody _body;

        public GuiDialogBodyContents(ICoreClientAPI capi, EntitySleepingBody body)
            : base(Lang.Get("offlinebodies:dialog-title", body.OwnerName), capi)
        {
            _body = body;
            Compose();
        }

        private void Compose()
        {
            var inv = _body.Inventory!;
            int cols = Math.Min(10, Math.Max(1, inv.Count));
            int rows = (int)Math.Ceiling(inv.Count / (double)cols);

            ElementBounds slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, GuiStyle.TitleBarHeight + 5, cols, rows);
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(slotBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

            SingleComposer = capi.Gui.CreateCompo("offlinebody-" + _body.EntityId, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(DialogTitle, () => TryClose())
                .AddItemSlotGrid(inv, SendInvPacket, cols, slotBounds, "slots")
                .Compose();
        }

        private void SendInvPacket(object packet)
        {
            capi.Network.SendEntityPacket(_body.EntityId, packet);
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            capi.World.Player.InventoryManager.OpenInventory(_body.Inventory);
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();
            capi.World.Player.InventoryManager.CloseInventory(_body.Inventory);
            _body.SendClosePacket();
        }

        public override void OnBeforeRenderFrame3D(float dt)
        {
            base.OnBeforeRenderFrame3D(dt);
            // Close if the body is gone or the player walked away.
            var self = capi.World.Player.Entity;
            if (!_body.Alive || self.Pos.DistanceTo(_body.Pos) > 5)
            {
                TryClose();
            }
        }

        public override bool PrefersUngrabbedMouse => false;
    }
}
