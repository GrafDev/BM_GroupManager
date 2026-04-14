using System;
using System.Collections.Generic;

namespace BM_GroupManager.Models
{
    public enum GroupStatus
    {
        Assembled,
        Disassembled
    }

    /// <summary>
    /// Запись о Типе группы, включающая список всех её разобранных экземпляров.
    /// </summary>
    public class GroupRecord
    {
        public Guid   TypeGuid     { get; set; }
        public string TypeName     { get; set; }
        public GroupStatus Status   { get; set; }
        public double TemplateRotation { get; set; }

        public List<InstanceRecord> Instances { get; set; } = new List<InstanceRecord>();
    }
}
