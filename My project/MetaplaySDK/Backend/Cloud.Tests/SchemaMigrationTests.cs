// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Cloud.Tests
{
    [SupportedSchemaVersions(1, 4)]
    public class MigrationTestModel : ISchemaMigratable
    {
        public int          SchemaVersion       { get; private set; }
        public List<int>    MigrationsExecuted  { get; private set; } = new List<int>();

        public MigrationTestModel(int schemaVersion)
        {
            SchemaVersion = schemaVersion;
        }

        [MigrationFromVersion(1)]
        public void Migrate1To2()
        {
            // Failed migration, throw an error
            MigrationsExecuted.Add(1);
            SchemaVersion = 2;
            throw new InvalidOperationException("Forced error");
        }

        [MigrationFromVersion(2)]
        public void Migrate2To3()
        {
            MigrationsExecuted.Add(2);
            SchemaVersion = 3;
        }

        [MigrationFromVersion(3)]
        public void Migrate3To4()
        {
            MigrationsExecuted.Add(3);
            SchemaVersion = 4;
        }
    }

    // \todo [petri] would be better to test in the Actor context, but it's not easily spawnable for testing purposes
    [TestFixture]
    public class SchemaMigrationTests
    {
        [Test]
        public void TestSuccessfulMigration()
        {
            MigrationTestModel model = new MigrationTestModel(2);
            SchemaMigrator migrator = SchemaMigrator.CreateForType(typeof(MigrationTestModel));
            migrator.RunMigrations(model, model.SchemaVersion);
            Assert.AreEqual(4, model.SchemaVersion);
            Assert.AreEqual(new List<int> { 2, 3 }, model.MigrationsExecuted);
        }

        [Test]
        public void TestFailedMigration()
        {
            MigrationTestModel model = new MigrationTestModel(1);
            SchemaMigrator migrator = SchemaMigrator.CreateForType(typeof(MigrationTestModel));
            try
            {
                migrator.RunMigrations(model, model.SchemaVersion);
                Assert.Fail("Was expecting a SchemaMigrationError");
            }
            catch (SchemaMigrationError error)
            {
                Assert.AreEqual(1, error.FromVersion); // failure in v1->v2 migration
            }
            catch
            {
                Assert.Fail("Was expecting a SchemaMigrationError");
            }
        }
    }
}
