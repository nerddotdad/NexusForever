using NexusForever.Network.Message;

namespace NexusForever.Network.World.Message.Model.Support
{
    [Message(GameMessageOpcode.ClientReportBug)]
    public class ClientReportBug : IReadable
    {
        /// <summary>Client UI allows 500 characters; buffer is typically 500 text + null wide char.</summary>
        private const int DescriptionWideCharSlots = 501;

        /// <summary>
        /// BugCategory.tbl row id (primary dropdown, e.g. Art, UI, Quest).
        /// </summary>
        public ushort BugCategoryTableId { get; private set; }

        /// <summary>
        /// BugSubcategory.tbl row id (secondary dropdown). Sent as 16 bits (same pattern as <see cref="ClientSupportTicket"/> categories).
        /// </summary>
        public ushort BugSubcategoryTableId { get; private set; }

        public uint SelectedUnitId { get; private set; }
        public uint Quest2Id { get; private set; }
        public string Description { get; private set; }

        public void Read(GamePacketReader reader)
        {
            BugCategoryTableId      = reader.ReadUShort();
            BugSubcategoryTableId   = reader.ReadUShort();
            SelectedUnitId          = reader.ReadUInt();
            Quest2Id                = reader.ReadUInt();
            // Fixed UTF-16 buffer (500 chars + terminating null in UI); not ReadWideString() (bit-packed length).
            Description = reader.ReadWideStringBlock(DescriptionWideCharSlots);
        }
    }
}
