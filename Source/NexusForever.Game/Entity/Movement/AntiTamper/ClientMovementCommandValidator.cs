using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement.AntiTamper;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Entity.Movement.Command.State;

namespace NexusForever.Game.Entity.Movement.AntiTamper
{
    public class ClientMovementCommandValidator : IClientMovementCommandValidator
    {
        /// <summary>
        /// Validate the time between the client and server to ensure the client is not tampering with the time.
        /// </summary>
        public void ValidateTime(uint clientTime, uint serverTime)
        {
            // TODO
            int difference = (int)clientTime - (int)serverTime;
        }

        /// <summary>
        /// Validate the position from the client to ensure the client is not tampering with the position.
        /// </summary>
        public void ValidatePosition()
        {
            // TODO
        }

        /// <summary>
        /// Validate the mode from the client to ensure the client is not tampering with the mode.
        /// </summary>
        public void ValidateMode()
        {
            // TODO
        }

        /// <summary>
        /// Validate the state from the client to ensure the client is not tampering with the state.
        /// </summary>
        public void ValidateState(IWorldEntity entity, StateFlags state)
        {
            if ((state & StateFlags.Sprint) != 0)
            {
                if (entity is IUnitEntity unitEntity)
                {
                    float currentEndurance = unitEntity.GetStatFloat(Stat.Resource0) ?? 0f;
                    if (currentEndurance <= 0f)
                    {
                        return;
                    }
                }
            }
        }
    }
}
