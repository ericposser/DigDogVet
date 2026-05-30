using DigDog.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DigDog.Data;

public class Contexto : IdentityDbContext<IdentityUser>
{
    public Contexto(DbContextOptions<Contexto> options) : base(options)
    {
    }

    public DbSet<Cliente> Cliente { get; set; }
    public DbSet<Pet> Pet { get; set; }
    public DbSet<Consulta> Consulta { get; set; }
    public DbSet<BanhoTosa> BanhoTosa { get; set; }
    public DbSet<Vacina> Vacina { get; set; }
    public DbSet<Vacinacao> Vacinacao { get; set; }
    public DbSet<Produto> Produto { get; set; }
    public DbSet<Venda> Venda { get; set; }
    public DbSet<KanbanToken> KanbanToken { get; set; }
    public DbSet<CarteiraToken> CarteiraToken { get; set; }
    public DbSet<RolePermissao> RolePermissao { get; set; }
    public DbSet<Configuracao> Configuracao { get; set; }
    public DbSet<Empresa> Empresa { get; set; }
    public DbSet<EmpresaUsuario> EmpresaUsuario { get; set; }
    public DbSet<Log> Log { get; set; }
    public DbSet<Receituario> Receituario { get; set; }
    public DbSet<Assinatura> Assinatura { get; set; }

    // Expõe a tabela AspNetUserClaims do Identity para permitir queries
    // diretas por claim (ex: busca por CPF no CaktoWebhookService) sem
    // precisar carregar todos os usuários em memória.
    public DbSet<IdentityUserClaim<string>> UserClaims =>
        Set<IdentityUserClaim<string>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Empresa ───────────────────────────────────────────────────────
        modelBuilder.Entity<Empresa>(b =>
        {
            b.Property(e => e.IdAdmin).HasColumnName("IdAdmin");

            b.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(e => e.IdAdmin)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── EmpresaUsuario ────────────────────────────────────────────────
        modelBuilder.Entity<EmpresaUsuario>(b =>
        {
            b.Property(eu => eu.IdEmpresa).HasColumnName("IdEmpresa");
            b.Property(eu => eu.IdUsuario).HasColumnName("IdUsuario");

            b.HasIndex(eu => new { eu.IdEmpresa, eu.IdUsuario }).IsUnique();

            b.HasOne(eu => eu.Empresa)
                .WithMany(e => e.Usuarios)
                .HasForeignKey(eu => eu.IdEmpresa)
                .HasConstraintName("FK_EmpresaUsuario_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Configuracao ──────────────────────────────────────────────────
        modelBuilder.Entity<Configuracao>(b =>
        {
            b.Property(c => c.IdEmpresa).HasColumnName("IdEmpresa");

            b.HasOne(c => c.Empresa)
                .WithOne(e => e.Configuracao)
                .HasForeignKey<Configuracao>(c => c.IdEmpresa)
                .HasConstraintName("FK_Configuracao_Empresa_IdEmpresa")
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Cliente ───────────────────────────────────────────────────────
        modelBuilder.Entity<Cliente>(b =>
        {
            b.Property(c => c.IdEmpresa).HasColumnName("IdEmpresa");

            b.HasMany(c => c.Pets)
                .WithOne(p => p.Cliente)
                .HasForeignKey(p => p.IdCliente)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(c => c.Empresa)
                .WithMany(e => e.Clientes)
                .HasForeignKey(c => c.IdEmpresa)
                .HasConstraintName("FK_Cliente_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Pet ───────────────────────────────────────────────────────────
        modelBuilder.Entity<Pet>(b =>
        {
            b.Property(p => p.IdEmpresa).HasColumnName("IdEmpresa");

            b.HasMany(p => p.Consulta)
                .WithOne(c => c.Pet)
                .HasForeignKey(c => c.IdPet)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(p => p.BanhoTosa)
                .WithOne(bt => bt.Pet)
                .HasForeignKey(bt => bt.IdPet)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(p => p.Empresa)
                .WithMany(e => e.Pets)
                .HasForeignKey(p => p.IdEmpresa)
                .HasConstraintName("FK_Pet_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Consulta ──────────────────────────────────────────────────────
        modelBuilder.Entity<Consulta>(b =>
        {
            b.Property(c => c.IdEmpresa).HasColumnName("IdEmpresa");

            b.HasOne(c => c.Empresa)
                .WithMany(e => e.Consultas)
                .HasForeignKey(c => c.IdEmpresa)
                .HasConstraintName("FK_Consulta_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Vacina ────────────────────────────────────────────────────────
        modelBuilder.Entity<Vacina>(b =>
        {
            b.Property(v => v.IdEmpresa).HasColumnName("IdEmpresa");

            b.HasMany(v => v.Vacinacoes)
                .WithOne(va => va.Vacina)
                .HasForeignKey(va => va.IdVacina)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(v => v.Empresa)
                .WithMany(e => e.Vacinas)
                .HasForeignKey(v => v.IdEmpresa)
                .HasConstraintName("FK_Vacina_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Vacinacao ─────────────────────────────────────────────────────
        modelBuilder.Entity<Vacinacao>(b =>
        {
            b.Property(v => v.IdEmpresa).HasColumnName("IdEmpresa");

            b.HasOne(v => v.Empresa)
                .WithMany(e => e.Vacinacoes)
                .HasForeignKey(v => v.IdEmpresa)
                .HasConstraintName("FK_Vacinacao_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── BanhoTosa ─────────────────────────────────────────────────────
        modelBuilder.Entity<BanhoTosa>(b =>
        {
            b.Property(bt => bt.IdEmpresa).HasColumnName("IdEmpresa");

            b.HasOne(bt => bt.Empresa)
                .WithMany(e => e.BanhosTosas)
                .HasForeignKey(bt => bt.IdEmpresa)
                .HasConstraintName("FK_BanhoTosa_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Produto ───────────────────────────────────────────────────────
        modelBuilder.Entity<Produto>(b =>
        {
            b.Property(p => p.IdEmpresa).HasColumnName("IdEmpresa");

            b.HasMany(p => p.Venda)
                .WithOne(v => v.Produto)
                .HasForeignKey(v => v.IdProduto)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasOne(p => p.Empresa)
                .WithMany(e => e.Produtos)
                .HasForeignKey(p => p.IdEmpresa)
                .HasConstraintName("FK_Produto_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Venda ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Venda>(b =>
        {
            b.Property(v => v.IdEmpresa).HasColumnName("IdEmpresa");

            b.HasOne(v => v.Empresa)
                .WithMany(e => e.Vendas)
                .HasForeignKey(v => v.IdEmpresa)
                .HasConstraintName("FK_Venda_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── CarteiraToken ─────────────────────────────────────────────────
        modelBuilder.Entity<CarteiraToken>(b =>
        {
            b.Property(t => t.IdEmpresa).HasColumnName("IdEmpresa");
            b.Property(t => t.IdPet).HasColumnName("IdPet");

            b.HasOne(t => t.Pet)
                .WithMany()
                .HasForeignKey(t => t.IdPet)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(t => t.Empresa)
                .WithMany()
                .HasForeignKey(t => t.IdEmpresa)
                .HasConstraintName("FK_CarteiraToken_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── KanbanToken ───────────────────────────────────────────────────
        modelBuilder.Entity<KanbanToken>(b =>
        {
            b.Property(t => t.IdEmpresa).HasColumnName("IdEmpresa");
            b.Property(t => t.IdBanhoTosa).HasColumnName("IdBanhoTosa");

            b.HasOne(t => t.BanhoTosa)
                .WithMany()
                .HasForeignKey(t => t.IdBanhoTosa)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(t => t.Empresa)
                .WithMany()
                .HasForeignKey(t => t.IdEmpresa)
                .HasConstraintName("FK_KanbanToken_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Receituario ───────────────────────────────────────────────────
        modelBuilder.Entity<Receituario>(b =>
        {
            b.Property(r => r.IdEmpresa).HasColumnName("IdEmpresa");
            b.Property(r => r.IdConsulta).HasColumnName("IdConsulta");

            b.Property(r => r.MedicamentosJson)
                .HasColumnType("LONGTEXT");

            b.HasOne(r => r.Empresa)
                .WithMany()
                .HasForeignKey(r => r.IdEmpresa)
                .HasConstraintName("FK_Receituario_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(r => r.Consulta)
                .WithMany()
                .HasForeignKey(r => r.IdConsulta)
                .HasConstraintName("FK_Receituario_Consulta_IdConsulta")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── RolePermissao ─────────────────────────────────────────────────
        modelBuilder.Entity<RolePermissao>(b =>
        {
            b.HasIndex(rp => new { rp.RoleId, rp.Permissao }).IsUnique();
        });

        // ── Log ───────────────────────────────────────────────────────────
        modelBuilder.Entity<Log>(b =>
        {
            b.Property(l => l.IdEmpresa).HasColumnName("IdEmpresa");

            b.HasOne(l => l.Empresa)
                .WithMany()
                .HasForeignKey(l => l.IdEmpresa)
                .HasConstraintName("FK_Log_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Assinatura ────────────────────────────────────────────────────
        modelBuilder.Entity<Assinatura>(b =>
        {
            b.Property(a => a.IdEmpresa).HasColumnName("IdEmpresa");

            b.HasOne(a => a.Empresa)
                .WithMany(e => e.Assinaturas)
                .HasForeignKey(a => a.IdEmpresa)
                .HasConstraintName("FK_Assinatura_Empresa_IdEmpresa")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
