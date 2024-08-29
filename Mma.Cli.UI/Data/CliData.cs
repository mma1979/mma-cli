using Microsoft.EntityFrameworkCore;

using Mma.Cli.Shared.Builders;
using Mma.Cli.Shared.Consts;
using Mma.Cli.Shared.Data;
using Mma.Cli.Shared.Helpers;
using Mma.Cli.Shared.Models;
using Mma.Cli.UI.Models;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mma.Cli.UI.Data
{
    public class CliData
    {
        private readonly CliDbContext _context;

        public CliData(CliDbContext context)
        {
            _context = context;
        }

        public RelationModel TorelationModel(RelationDto dto) {
        
        var parent = _context.Entities.FirstOrDefault(e=>e.EntityName == dto.ParentEntityName);
        var child = _context.Entities.FirstOrDefault(e=>e.EntityName == dto.ChiledEntityName);

            return new RelationModel
            {
                ChildId = child.Id,
                Name = $"{dto.ParentEntityName}_{dto.ChiledEntityName}_{dto.ParentEntityName}Id",
                ParentId = parent.Id,
                Applied = false,
            };

        }

        public async Task<(ProjectModel?,List<EntityModel>?)> Load(int projectId)
        {
            var project = await _context.Projects.FirstOrDefaultAsync(e => e.Id == projectId);
            var entities = await _context.Entities
            .Include(e => e.Rows)
            .Where(e => e.ProjectId == projectId)
            .ToListAsync();

            return (project, entities);
        }

        public async Task<EntityModel> AddEntity(EntityModel model)
        {

            var record = await _context.Entities.AddAsync(model);
            await _context.SaveChangesAsync();
            return record.Entity;

        }

        public async Task<List<RelationDto>> LoadRelations(int projectId)
        {
            var relations = await _context.Relations
            .Include(r => r.ParentEntity)
            .Include(r => r.ChiledEntity)
            .Where(r => r.ParentEntity.ProjectId == projectId)
            .Select(r => new RelationDto
            {
                Id = r.Id,
                Name = r.Name,
                ParentId = r.ParentId,
                ParentEntityName = r.ParentEntity.EntityName,
                ChildId = r.ChildId,
                ChiledEntityName = r.ChiledEntity.EntityName
            }).ToListAsync();

            return relations ?? new();
        }

       public  async Task DeleteRelation(RelationDto row)
        {

            var relation = await _context.Relations.FindAsync(row.Id);
            _context.Relations.Remove(relation);
            await _context.SaveChangesAsync();
           
        }

        public bool MigrateEntity(EntityModel model, string solutionPath)
        {
            string[] args = { "g", "e", model.EntityName, model.PkType, "--mapper", BuildHelper.DetectMapper(solutionPath) };
            EntityBuilder.New(args, solutionPath)
                    .GenerateModels()
                    .GenerateValidator()
                    .GenerateEntity()
                    .GenerateEntityConfig()
                    .DbContextMapping()
                    .GenerateService()
                    .GenerateController(!args.Contains(Flags.ApiFlag));

            return true;
        }

        public bool MigrateProperty(EntityRowModel model, string entityName, string solutionPath)
        {
            string[] args = { "g", "p", entityName, model.ColumnName, model.DataType, $"{model.Nullable}" };
            PropertiesBuilder.New(args, BuildHelper.DetectMapper(solutionPath), solutionPath)
                   .UpdateEntityModels()
                   .UpdateEntity()
                   .UpdateEntityConfig();

            return true;
        }

        public bool MigrateRelation(RelationModel model, string solutionPath)
        {
            string[] args = { "g", "r", model.ParentEntity.EntityName, model.ChiledEntity.EntityName, $"{model.ParentEntity.EntityName}Id", model.ParentEntity.PkType };
            RelationsBuilder.New(args, solutionPath)
                       .UpdateParentDtos()
                       .UpdateChildDtos()
                       .UpdateParentEntity()
                       .UpdateChildEntity()
                       .UpdateParentEntityConfig();


            return true;
        }

        public async Task<bool> RemoveEntity(EntityModel entity)
        {
            EntityModel? model = await _context.Entities
                .FirstOrDefaultAsync(e => e.ProjectId == entity.ProjectId && e.EntityName == entity.EntityName);
            _context.Entities .Remove(model!);
            return (await _context.SaveChangesAsync()) > 0;
        } 
    }
}
