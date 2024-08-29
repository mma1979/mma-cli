using Microsoft.EntityFrameworkCore;

using Mma.Cli.Shared.Models;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mma.Cli.Shared.Data
{
    public static class ProjectDumber
    {

        public static void DumpProject(this CliDbContext ctx, int projectId, string solutionDir)
        {
            var project = ctx.Projects
                .AsNoTracking()
                .First(p => p.Id == projectId);

            project.Entities = new();

            var entities = ctx.Entities
                .AsNoTracking()
                .Where(e => e.ProjectId == projectId)
                .ToList();

            List<EntityRowModel>? rows = null;
            List<RelationModel>? relations = null;

            if (entities.Any())
            {

                rows = (from r in ctx.EntityRows.AsNoTracking()
                        join e in ctx.Entities.AsNoTracking()
                        on r.EntityId equals e.Id
                        where e.ProjectId == projectId
                        select r).ToList();


                var parentRelations = (from r in ctx.Relations.AsNoTracking()
                                       join e in ctx.Entities.AsNoTracking()
                                       on r.ParentId equals e.Id
                                       where e.ProjectId == projectId
                                       select r).ToList();

                var childRelations = (from r in ctx.Relations.AsNoTracking()
                                      join e in ctx.Entities.AsNoTracking()
                                      on r.ChildId equals e.Id
                                      where e.ProjectId == projectId
                                      select r).ToList();

                parentRelations.AddRange(childRelations);

                relations = parentRelations
                    .DistinctBy(r => r.Id)
                    .ToList();
            }

            var data = new
            {
                Project = project ?? new(),
                Entities = entities ?? new(),
                Rows = rows ?? new(),
                Relations = relations ?? new()
            };


            var json = JsonConvert.SerializeObject(data, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            });

            var mmaDir = Directory.CreateDirectory(Path.Combine(solutionDir, ".mma"));

            using StreamWriter writer = new(Path.Combine(mmaDir.FullName, "project.mma"));
            writer.Write(json);
            writer.Flush();
            writer.Close();
        }
    }
}
