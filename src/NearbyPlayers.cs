using System;
using System.Collections.Generic;

namespace ESPWorkbench
{
    public static class NearbyPlayers
    {
        // Count all detected characters and training targets, independently of display filters.
        public static int Count(Frame frame,RenderOptions options)
        {
            if(!Painter.Fresh(frame)||!options.EspEnabled||!Vec3.Good(frame.UnitsPerMeter)||frame.UnitsPerMeter<=0)return 0;
            int count=0;
            HashSet<string> addresses=new HashSet<string>(StringComparer.Ordinal);
            foreach(Actor actor in frame.Actors)
            {
                if(actor==null||(!String.IsNullOrEmpty(actor.Kind)&&actor.Kind!="Character"&&actor.Kind!="TrainingTarget"))continue;
                if(actor.Kind!="TrainingTarget"&&frame.LocalTeam>=0&&actor.Team==frame.LocalTeam)continue;
                if(!actor.Position.Finite||!CharacterLife.Retain(actor.Health,actor.MaxHealth,actor.IsDead,actor.NearDeathBreath))continue;
                float distance=actor.Position.Distance(frame.Origin)/frame.UnitsPerMeter;
                if(!Vec3.Good(distance)||distance>Math.Min(options.MaxDistance,options.Category(actor.Kind).MaxDistance))continue;
                if(!String.IsNullOrEmpty(actor.Address)&&!addresses.Add(actor.Address))continue;
                count++;
            }
            return count;
        }
    }
}
