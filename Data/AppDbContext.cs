using DataForge.Collections;
using DataForge.Fields;
using DataForge.Folders;
using DataForge.Relations;
using DataForge.Upload;
using DataForge.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DataForge.Data;

public class AppDbContext : IdentityDbContext<User, IdentityRole, string>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<CollectionMeta> CollectionMetas => Set<CollectionMeta>();
    public DbSet<FieldMeta> FieldMetas => Set<FieldMeta>();
    public DbSet<FileMeta> FileMetas => Set<FileMeta>();
    public DbSet<RelationMeta> RelationMetas => Set<RelationMeta>();
    public DbSet<FolderMeta> FolderMetas => Set<FolderMeta>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity tables
        builder.Entity<User>().ToTable("users");
        builder.Entity<IdentityRole>().ToTable("roles");
        builder.Entity<IdentityUserRole<string>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<string>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("user_logins");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("role_claims");
        builder.Entity<IdentityUserToken<string>>().ToTable("user_tokens");

        // CollectionMeta
        builder.Entity<CollectionMeta>(e =>
        {
            e.ToTable("collection_metas");
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.TableName).IsUnique();
            e.HasIndex(c => c.Name).IsUnique();
            e.Property(c => c.Name).HasMaxLength(50);
            e.Property(c => c.TableName).HasMaxLength(63);
            e.Property(c => c.Singleton).HasDefaultValue(false);
            e.Property(c => c.PrimaryKey).HasMaxLength(50).HasDefaultValue("id");
            e.Property(c => c.PkType).HasMaxLength(20).HasDefaultValue("auto-increment");
            e.Property(c => c.Id).ValueGeneratedOnAdd();
        });

        // FieldMeta
        builder.Entity<FieldMeta>(e =>
        {
            e.ToTable("field_metas");
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).ValueGeneratedOnAdd();
            e.Property(f => f.CollectionId);
            e.Property(f => f.Name).HasMaxLength(63);
            e.Property(f => f.Label).HasMaxLength(100);
            e.Property(f => f.Type).HasConversion<string>();
            e.Property(f => f.Width).HasMaxLength(10).HasDefaultValue("full");
            e.Property(f => f.Note).HasMaxLength(500);
            e.Property(f => f.Interface).HasMaxLength(50);
            e.Property(f => f.DefaultValue).HasMaxLength(255);
            e.Property(f => f.MaxLength);
            e.Property(f => f.IsUnique).HasDefaultValue(false);
            e.Property(f => f.IsIndexed).HasDefaultValue(false);
            e.HasOne(f => f.Collection)
             .WithMany(c => c.Fields)
             .HasForeignKey(f => f.CollectionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // FileMeta
        builder.Entity<FileMeta>(e =>
        {
            e.ToTable("file_metas");
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
            e.Property(f => f.FilenameDisk).HasColumnName("filename_disk").HasMaxLength(255).IsRequired();
            e.Property(f => f.FilenameDownload).HasColumnName("filename_download").HasMaxLength(255).IsRequired();
            e.Property(f => f.Title).HasColumnName("title").HasMaxLength(255);
            e.Property(f => f.Type).HasColumnName("type").HasMaxLength(100).IsRequired();
            e.Property(f => f.Filesize).HasColumnName("filesize");
            e.Property(f => f.Width).HasColumnName("width");
            e.Property(f => f.Height).HasColumnName("height");
            e.Property(f => f.UploadedBy).HasColumnName("uploaded_by");
            e.Property(f => f.UploadedOn).HasColumnName("uploaded_on").HasDefaultValueSql("NOW()");
            e.Property(f => f.FolderId).HasColumnName("folder_id");
            e.HasOne(f => f.Folder)
             .WithMany(fo => fo.Files)
             .HasForeignKey(f => f.FolderId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // RelationMeta
        builder.Entity<RelationMeta>(e =>
        {
            e.ToTable("relation_metas");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).ValueGeneratedOnAdd();
            e.Property(r => r.ManyCollection).HasColumnName("many_collection").HasMaxLength(63).IsRequired();
            e.Property(r => r.ManyField).HasColumnName("many_field").HasMaxLength(63).IsRequired();
            e.Property(r => r.OneCollection).HasColumnName("one_collection").HasMaxLength(63).IsRequired();
            e.Property(r => r.OneField).HasColumnName("one_field").HasMaxLength(63);
            e.Property(r => r.OnDelete).HasColumnName("on_delete").HasMaxLength(20).HasDefaultValue("SET NULL");
            e.Property(r => r.OnDeselect).HasColumnName("on_deselect").HasMaxLength(20);
            e.HasIndex(r => new { r.ManyCollection, r.ManyField }).IsUnique();
        });

        // FolderMeta
        builder.Entity<FolderMeta>(e =>
        {
            e.ToTable("folders");
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(f => f.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
            e.Property(f => f.ParentId).HasColumnName("parent_id");
            e.Property(f => f.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
            e.Property(f => f.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
            e.HasOne(f => f.Parent)
             .WithMany(f => f.Children)
             .HasForeignKey(f => f.ParentId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
