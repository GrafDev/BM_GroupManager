using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BM_GroupManager.Models;

namespace BM_GroupManager.Services
{
    public static class GroupRegistryService
    {
        // GUID для схемы Типа группы (v6)
        private static readonly Guid TypeSchemaGuid = new Guid("4A5B6C7D-8E9F-0A1B-2C3D-4E5F6A7B8C92");
        // GUID для схемы Экземпляра (v6)
        private static readonly Guid InstanceSchemaGuid = new Guid("11223344-5566-7788-9900-AABBCCDDEEFD");

        private const string SchemaNameType = "SGM_GroupTypeRecord_v6";
        private const string SchemaNameInst = "SGM_InstanceLocation_v6";

        // ── Схемы ────────────────────────────────────────────────────────────────

        private static Schema GetInstanceSchema()
        {
            Schema existing = Schema.Lookup(InstanceSchemaGuid);
            if (existing != null) return existing;

            SchemaBuilder builder = new SchemaBuilder(InstanceSchemaGuid);
            builder.SetSchemaName(SchemaNameInst);
            builder.AddSimpleField("InstanceId", typeof(Guid));
            
            // В Revit 2021+ для double НУЖНО указывать Spec (Length/Angle)
            builder.AddSimpleField("X", typeof(double)).SetSpec(SpecTypeId.Length);
            builder.AddSimpleField("Y", typeof(double)).SetSpec(SpecTypeId.Length);
            builder.AddSimpleField("Z", typeof(double)).SetSpec(SpecTypeId.Length);
            builder.AddSimpleField("Rotation", typeof(double)).SetSpec(SpecTypeId.Angle);
            
            return builder.Finish();
        }

        private static Schema GetTypeSchema()
        {
            Schema existing = Schema.Lookup(TypeSchemaGuid);
            if (existing != null) return existing;

            Schema instSchema = GetInstanceSchema();

            SchemaBuilder builder = new SchemaBuilder(TypeSchemaGuid);
            builder.SetSchemaName(SchemaNameType);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            
            builder.AddSimpleField("TypeGuid", typeof(Guid));
            builder.AddSimpleField("TypeName", typeof(string));
            builder.AddSimpleField("Status", typeof(int));
            
            FieldBuilder fieldBuilder = builder.AddArrayField("Instances", typeof(Entity));
            fieldBuilder.SetSubSchemaGUID(InstanceSchemaGuid);
            
            return builder.Finish();
        }

        // ── Работа с данными ─────────────────────────────────────────────────────

        public static List<GroupRecord> GetAll(Document doc)
        {
            Schema typeSchema = GetTypeSchema();
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage));

            List<GroupRecord> results = new List<GroupRecord>();

            foreach (DataStorage ds in collector)
            {
                Entity typeEnt = ds.GetEntity(typeSchema);
                if (!typeEnt.IsValid()) continue;

                GroupRecord record = new GroupRecord
                {
                    TypeGuid = typeEnt.Get<Guid>("TypeGuid"),
                    TypeName = typeEnt.Get<string>("TypeName"),
                    Status   = (GroupStatus)typeEnt.Get<int>("Status")
                };

                IList<Entity> instEntities = typeEnt.Get<IList<Entity>>("Instances");
                foreach (Entity e in instEntities)
                {
                    record.Instances.Add(new InstanceRecord
                    {
                        InstanceId    = e.Get<Guid>("InstanceId"),
                        // ПРИМЕЧАНИЕ: При чтении Measurable-полей НУЖНО указывать UnitTypeId
                        InsertionX    = e.Get<double>("X", UnitTypeId.Feet),
                        InsertionY    = e.Get<double>("Y", UnitTypeId.Feet),
                        InsertionZ    = e.Get<double>("Z", UnitTypeId.Feet),
                        RotationAngle = e.Get<double>("Rotation", UnitTypeId.Radians)
                    });
                }
                results.Add(record);
            }
            return results;
        }

        public static void Register(Document doc, GroupRecord record)
        {
            Schema typeSchema = GetTypeSchema();
            Schema instSchema = GetInstanceSchema();

            DataStorage storage = FindStorage(doc, record.TypeGuid) ?? DataStorage.Create(doc);

            Entity typeEnt = new Entity(typeSchema);
            typeEnt.Set("TypeGuid", record.TypeGuid);
            typeEnt.Set("TypeName", record.TypeName ?? "");
            typeEnt.Set("Status",   (int)record.Status);

            List<Entity> instEntities = new List<Entity>();
            foreach (var inst in record.Instances)
            {
                Entity e = new Entity(instSchema);
                e.Set("InstanceId", inst.InstanceId);
                // ПРИМЕЧАНИЕ: При записи Measurable-полей НУЖНО указывать UnitTypeId
                e.Set("X", inst.InsertionX, UnitTypeId.Feet);
                e.Set("Y", inst.InsertionY, UnitTypeId.Feet);
                e.Set("Z", inst.InsertionZ, UnitTypeId.Feet);
                e.Set("Rotation", inst.RotationAngle, UnitTypeId.Radians);
                instEntities.Add(e);
            }
            typeEnt.Set<IList<Entity>>("Instances", instEntities);

            storage.SetEntity(typeEnt);
        }

        public static void Unregister(Document doc, Guid typeGuid)
        {
            DataStorage storage = FindStorage(doc, typeGuid);
            if (storage != null)
            {
                doc.Delete(storage.Id);
            }
        }

        public static void Update(Document doc, GroupRecord record)
        {
            Register(doc, record);
        }

        private static DataStorage FindStorage(Document doc, Guid typeGuid)
        {
            Schema schema = GetTypeSchema();
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(ds => 
                {
                    Entity ent = ds.GetEntity(schema);
                    return ent.IsValid() && ent.Get<Guid>("TypeGuid") == typeGuid;
                });
        }
    }
}
