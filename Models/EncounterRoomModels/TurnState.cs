namespace dndhelper.Models.EncounterRoomModels
{
    public class TurnState
    {
        public int Round { get; set; } = 0;
        public int CurrentIndex { get; set; } = 0;
        public bool IsActive { get; set; } = false;
    }
}
