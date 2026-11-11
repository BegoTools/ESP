using System;

namespace ESPWorkbench
{
    public static class CharacterLife
    {
        public static bool Knocked(float health,bool? dead,float? breath)
        {
            return Vec3.Good(health)&&health==0&&dead==false&&breath.HasValue&&Vec3.Good(breath.Value)&&breath.Value>0&&breath.Value<=100000;
        }
        public static bool Retain(float health,float maximum,bool? dead,float? breath)
        {
            return dead!=true&&Vec3.Good(health)&&Vec3.Good(maximum)&&maximum>0&&maximum<=100000&&health>=0&&health<=maximum*1.1f&&(health>0||Knocked(health,dead,breath));
        }
    }
}
