using System;
using Autodesk.Revit.DB;

namespace BM_GroupManager.Models
{
    /// <summary>
    /// Запись о конкретном экземпляре группы (положение и поворот).
    /// </summary>
    public class InstanceRecord
    {
        public Guid InstanceId      { get; set; }
        public double InsertionX    { get; set; }
        public double InsertionY    { get; set; }
        public double InsertionZ    { get; set; }
        public double RotationAngle { get; set; }

        public XYZ InsertionPoint => new XYZ(InsertionX, InsertionY, InsertionZ);
    }
}
