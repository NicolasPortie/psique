using Psique.Api.Data.Cifra;
using Psique.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Psique.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ContaPsicologo> ContasPsicologo => Set<ContaPsicologo>();
    public DbSet<PlanoSaas> PlanosSaas => Set<PlanoSaas>();
    public DbSet<AssinaturaConta> AssinaturasConta => Set<AssinaturaConta>();
    public DbSet<FaturaAssinatura> FaturasAssinatura => Set<FaturaAssinatura>();
    public DbSet<ConfiguracaoSegurancaConta> ConfiguracoesSegurancaConta => Set<ConfiguracaoSegurancaConta>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<PerfilAcesso> PerfisAcesso => Set<PerfilAcesso>();
    public DbSet<DadosProfissionaisPsicologo> DadosProfissionaisPsicologo => Set<DadosProfissionaisPsicologo>();
    public DbSet<Paciente> Pacientes => Set<Paciente>();
    public DbSet<ContatoPaciente> ContatosPaciente => Set<ContatoPaciente>();
    public DbSet<ConfiguracaoAtendimento> ConfiguracoesAtendimento => Set<ConfiguracaoAtendimento>();
    public DbSet<TipoAtendimento> TiposAtendimento => Set<TipoAtendimento>();
    public DbSet<HorarioDisponivel> HorariosDisponiveis => Set<HorarioDisponivel>();
    public DbSet<BloqueioAgenda> BloqueiosAgenda => Set<BloqueioAgenda>();
    public DbSet<Consulta> Consultas => Set<Consulta>();
    public DbSet<HistoricoConsulta> HistoricosConsulta => Set<HistoricoConsulta>();
    public DbSet<Prontuario> Prontuarios => Set<Prontuario>();
    public DbSet<RegistroSessao> RegistrosSessao => Set<RegistroSessao>();
    public DbSet<EvolucaoProntuario> EvolucoesProntuario => Set<EvolucaoProntuario>();
    public DbSet<DocumentoProntuario> DocumentosProntuario => Set<DocumentoProntuario>();
    public DbSet<VersaoDocumentoProntuario> VersoesDocumentoProntuario => Set<VersaoDocumentoProntuario>();
    public DbSet<ModeloDocumentoPsicologico> ModelosDocumentoPsicologico => Set<ModeloDocumentoPsicologico>();
    public DbSet<RegraDocumentoPsicologico> RegrasDocumentoPsicologico => Set<RegraDocumentoPsicologico>();
    public DbSet<ProtocoloEntregaDocumento> ProtocolosEntregaDocumento => Set<ProtocoloEntregaDocumento>();
    public DbSet<RetificacaoRegistroSessao> RetificacoesRegistroSessao => Set<RetificacaoRegistroSessao>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();
    public DbSet<Lembrete> Lembretes => Set<Lembrete>();
    public DbSet<ModeloMensagem> ModelosMensagem => Set<ModeloMensagem>();
    public DbSet<ListaEspera> ListaEspera => Set<ListaEspera>();
    public DbSet<IntegracaoWhatsApp> IntegracoesWhatsApp => Set<IntegracaoWhatsApp>();
    public DbSet<ConversaWhatsApp> ConversasWhatsApp => Set<ConversaWhatsApp>();
    public DbSet<MensagemWhatsApp> MensagensWhatsApp => Set<MensagemWhatsApp>();
    public DbSet<EventoBridgeWhatsApp> EventosBridgeWhatsApp => Set<EventoBridgeWhatsApp>();
    public DbSet<SessaoBot> SessoesBot => Set<SessaoBot>();
    public DbSet<IntencaoBot> IntencoesBot => Set<IntencaoBot>();
    public DbSet<LogAuditoria> LogsAuditoria => Set<LogAuditoria>();
    public DbSet<Notificacao> Notificacoes => Set<Notificacao>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(80);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PreencherHashesDeBusca();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PreencherHashesDeBusca();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>E-mail e telefone são cifrados; o hash ao lado é o que permite localizá-los.</summary>
    private void PreencherHashesDeBusca()
    {
        foreach (var entrada in ChangeTracker.Entries<Usuario>())
        {
            if (entrada.State is EntityState.Added || (entrada.State is EntityState.Modified && entrada.Property(usuario => usuario.Email).IsModified))
                entrada.Entity.EmailHash = string.IsNullOrWhiteSpace(entrada.Entity.Email) ? null : CifraDeColuna.HashEmail(entrada.Entity.Email);
        }
        foreach (var entrada in ChangeTracker.Entries<ConversaWhatsApp>())
        {
            if (entrada.State is EntityState.Added || (entrada.State is EntityState.Modified && entrada.Property(conversa => conversa.Telefone).IsModified))
                entrada.Entity.TelefoneHash = string.IsNullOrWhiteSpace(entrada.Entity.Telefone) ? null : CifraDeColuna.HashTelefone(entrada.Entity.Telefone);
        }
    }

    /// <summary>
    /// Toda propriedade com <see cref="CifradoAttribute"/> passa pela cifra de coluna: o banco guarda só o texto
    /// protegido, em nvarchar(max) (o tamanho cifrado não cabe nos limites do texto aberto; os limites seguem nos DTOs).
    /// </summary>
    private static void AplicarCifraDeColuna(ModelBuilder modelBuilder)
    {
        var conversorTexto = new ValueConverter<string, string>(valor => CifraDeColuna.Cifrar(valor), valor => CifraDeColuna.Decifrar(valor));
        var conversorData = new ValueConverter<DateOnly, string>(valor => CifraDeColuna.CifrarData(valor), valor => CifraDeColuna.DecifrarData(valor));
        foreach (var tipo in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var propriedade in tipo.GetProperties())
            {
                if (propriedade.PropertyInfo?.IsDefined(typeof(CifradoAttribute), inherit: true) != true) continue;
                var clr = Nullable.GetUnderlyingType(propriedade.ClrType) ?? propriedade.ClrType;
                if (clr == typeof(string)) propriedade.SetValueConverter(conversorTexto);
                else if (clr == typeof(DateOnly)) propriedade.SetValueConverter(conversorData);
                else throw new InvalidOperationException($"[Cifrado] não suporta {clr.Name} em {tipo.ClrType.Name}.{propriedade.Name}.");
                propriedade.SetMaxLength(null);
                propriedade.SetColumnType("nvarchar(max)");
            }
        }
    }

    /// <summary>Desligado só pelo teste que monta um banco legado, anterior à cifra de colunas.</summary>
    protected virtual bool CifrarColunas => true;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigurarContas(modelBuilder);
        ConfigurarAssinaturas(modelBuilder);
        ConfigurarConfiguracoesSeguranca(modelBuilder);
        ConfigurarUsuarios(modelBuilder);
        ConfigurarPacientes(modelBuilder);
        ConfigurarAgenda(modelBuilder);
        ConfigurarConsultas(modelBuilder);
        ConfigurarProntuarios(modelBuilder);
        ConfigurarDocumentosPsicologicos(modelBuilder);
        ConfigurarSessoes(modelBuilder);
        ConfigurarPagamentos(modelBuilder);
        ConfigurarLembretes(modelBuilder);
        ConfigurarWhatsApp(modelBuilder);
        ConfigurarBot(modelBuilder);
        ConfigurarAuditoria(modelBuilder);

        // Por último: vale para todas as entidades já configuradas acima.
        if (CifrarColunas) AplicarCifraDeColuna(modelBuilder);
    }

    private static void ConfigurarContas(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContaPsicologo>(entity =>
        {
            entity.ToTable("ContasPsicologo");
            entity.HasKey(conta => conta.Id);
            entity.Property(conta => conta.NomeConta).HasMaxLength(180).IsRequired();
            entity.HasIndex(conta => conta.Status);
        });
    }

    private static void ConfigurarAssinaturas(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<AssinaturaConta>(modelBuilder);
        ConfigurarEntidadeDaConta<FaturaAssinatura>(modelBuilder);

        modelBuilder.Entity<PlanoSaas>(entity =>
        {
            entity.ToTable("PlanosSaas", table =>
            {
                table.HasCheckConstraint("CK_PlanosSaas_ValorMensal", "[ValorMensal] >= 0");
                table.HasCheckConstraint("CK_PlanosSaas_ValorAnual", "[ValorAnual] IS NULL OR [ValorAnual] >= 0");
                table.HasCheckConstraint("CK_PlanosSaas_LimitePacientesAtivos", "[LimitePacientesAtivos] IS NULL OR [LimitePacientesAtivos] > 0");
                table.HasCheckConstraint("CK_PlanosSaas_LimiteMensagensWhatsAppMes", "[LimiteMensagensWhatsAppMes] IS NULL OR [LimiteMensagensWhatsAppMes] > 0");
                table.HasCheckConstraint("CK_PlanosSaas_LimiteDocumentosMes", "[LimiteDocumentosMes] IS NULL OR [LimiteDocumentosMes] > 0");
            });

            entity.HasKey(plano => plano.Id);
            entity.Property(plano => plano.Codigo).HasMaxLength(80).IsRequired();
            entity.Property(plano => plano.Nome).HasMaxLength(120).IsRequired();
            entity.Property(plano => plano.Descricao).HasMaxLength(1000);
            entity.Property(plano => plano.ValorMensal).HasPrecision(12, 2);
            entity.Property(plano => plano.ValorAnual).HasPrecision(12, 2);

            entity.HasIndex(plano => plano.Codigo).IsUnique();
            entity.HasIndex(plano => plano.Status);
        });

        modelBuilder.Entity<AssinaturaConta>(entity =>
        {
            entity.ToTable("AssinaturasConta");
            entity.HasKey(assinatura => assinatura.Id);

            entity.Property(assinatura => assinatura.MotivoCancelamento).HasMaxLength(1000);
            entity.Property(assinatura => assinatura.ProvedorPagamento).HasMaxLength(120);
            entity.Property(assinatura => assinatura.AssinaturaExternaId).HasMaxLength(180);

            entity.HasIndex(assinatura => new { assinatura.ContaPsicologoId, assinatura.Status });
            entity.HasIndex(assinatura => assinatura.ContaPsicologoId)
                .IsUnique()
                .HasFilter("[Status] = 'Ativa'");
            entity.HasIndex(assinatura => new { assinatura.ContaPsicologoId, assinatura.PlanoSaasId });
            entity.HasIndex(assinatura => new { assinatura.ProvedorPagamento, assinatura.AssinaturaExternaId })
                .IsUnique()
                .HasFilter("[ProvedorPagamento] IS NOT NULL AND [AssinaturaExternaId] IS NOT NULL");

            entity.HasOne(assinatura => assinatura.PlanoSaas)
                .WithMany(plano => plano.Assinaturas)
                .HasForeignKey(assinatura => assinatura.PlanoSaasId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FaturaAssinatura>(entity =>
        {
            entity.ToTable("FaturasAssinatura", table =>
            {
                table.HasCheckConstraint("CK_FaturasAssinatura_Valor", "[Valor] >= 0");
            });

            entity.HasKey(fatura => fatura.Id);

            entity.Property(fatura => fatura.Valor).HasPrecision(12, 2);
            entity.Property(fatura => fatura.ProvedorPagamento).HasMaxLength(120);
            entity.Property(fatura => fatura.FaturaExternaId).HasMaxLength(180);
            entity.Property(fatura => fatura.UrlPagamento).HasMaxLength(1000);

            entity.HasIndex(fatura => new { fatura.ContaPsicologoId, fatura.AssinaturaContaId, fatura.VencimentoEm });
            entity.HasIndex(fatura => new { fatura.ContaPsicologoId, fatura.Status, fatura.VencimentoEm });
            entity.HasIndex(fatura => new { fatura.ProvedorPagamento, fatura.FaturaExternaId })
                .IsUnique()
                .HasFilter("[ProvedorPagamento] IS NOT NULL AND [FaturaExternaId] IS NOT NULL");

            entity.HasOne(fatura => fatura.AssinaturaConta)
                .WithMany(assinatura => assinatura.Faturas)
                .HasPrincipalKey(assinatura => new { assinatura.ContaPsicologoId, assinatura.Id })
                .HasForeignKey(fatura => new { fatura.ContaPsicologoId, fatura.AssinaturaContaId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurarConfiguracoesSeguranca(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<ConfiguracaoSegurancaConta>(modelBuilder);

        modelBuilder.Entity<ConfiguracaoSegurancaConta>(entity =>
        {
            entity.ToTable("ConfiguracoesSegurancaConta", table =>
            {
                table.HasCheckConstraint("CK_ConfiguracoesSegurancaConta_MaximoTentativasInvalidas", "[MaximoTentativasInvalidas] > 0");
                table.HasCheckConstraint("CK_ConfiguracoesSegurancaConta_RetencaoLogsAuditoriaDias", "[RetencaoLogsAuditoriaDias] >= 365");
                table.HasCheckConstraint("CK_ConfiguracoesSegurancaConta_RetencaoProntuarioAnos", "[RetencaoProntuarioAnos] >= 5");
            });

            entity.HasKey(configuracao => configuracao.Id);
            entity.HasIndex(configuracao => configuracao.ContaPsicologoId).IsUnique();
        });
    }

    private static void ConfigurarEntidadeDaConta<TEntity>(ModelBuilder modelBuilder)
        where TEntity : EntidadeDaConta
    {
        modelBuilder.Entity<TEntity>().HasAlternateKey("ContaPsicologoId", "Id");
        modelBuilder.Entity<TEntity>().HasIndex(entidade => entidade.ContaPsicologoId);
        modelBuilder.Entity<TEntity>()
            .HasOne(entidade => entidade.ContaPsicologo)
            .WithMany()
            .HasForeignKey(entidade => entidade.ContaPsicologoId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurarUsuarios(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<Usuario>(modelBuilder);
        ConfigurarEntidadeDaConta<DadosProfissionaisPsicologo>(modelBuilder);
        ConfigurarEntidadeDaConta<PerfilAcesso>(modelBuilder);

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.ToTable("Usuarios");
            entity.HasKey(usuario => usuario.Id);

            entity.Property(usuario => usuario.NomeCompleto).HasMaxLength(180).IsRequired();
            entity.Property(usuario => usuario.Email).HasMaxLength(180).IsRequired();
            entity.Property(usuario => usuario.SenhaHash).HasMaxLength(500).IsRequired();
            entity.Property(usuario => usuario.Telefone).HasMaxLength(30);
            entity.Property(usuario => usuario.VersaoTermosAceitos).HasMaxLength(40);

            // O e-mail é cifrado; a unicidade e a busca usam o hash.
            entity.Property(usuario => usuario.EmailHash).HasMaxLength(64);
            entity.HasIndex(usuario => usuario.EmailHash).IsUnique().HasFilter("[EmailHash] IS NOT NULL");
            entity.HasIndex(usuario => new { usuario.ContaPsicologoId, usuario.Perfil, usuario.Status });

            entity.HasOne(usuario => usuario.PerfilAcesso)
                .WithMany(perfil => perfil.Usuarios)
                .HasPrincipalKey(perfil => new { perfil.ContaPsicologoId, perfil.Id })
                .HasForeignKey(usuario => new { usuario.ContaPsicologoId, usuario.PerfilAcessoId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PerfilAcesso>(entity =>
        {
            entity.ToTable("PerfisAcesso");
            entity.HasKey(perfil => perfil.Id);

            entity.Property(perfil => perfil.Nome).HasMaxLength(80).IsRequired();
            entity.Property(perfil => perfil.Descricao).HasMaxLength(300);
            entity.Property(perfil => perfil.Permissoes).HasMaxLength(400).IsRequired();

            entity.HasIndex(perfil => new { perfil.ContaPsicologoId, perfil.Nome }).IsUnique();
        });

        modelBuilder.Entity<DadosProfissionaisPsicologo>(entity =>
        {
            entity.ToTable("DadosProfissionaisPsicologo");
            entity.HasKey(dados => dados.Id);

            entity.Property(dados => dados.Crp).HasMaxLength(500).IsRequired();
            entity.Property(dados => dados.CrpHash).HasMaxLength(128).IsRequired();
            entity.Property(dados => dados.NomeProfissional).HasMaxLength(180).IsRequired();
            entity.Property(dados => dados.Bio).HasMaxLength(2000);
            entity.Property(dados => dados.EmailProfissional).HasMaxLength(180);
            entity.Property(dados => dados.TelefoneProfissional).HasMaxLength(30);

            entity.HasIndex(dados => new { dados.ContaPsicologoId, dados.UsuarioId }).IsUnique();
            entity.HasIndex(dados => new { dados.ContaPsicologoId, dados.CrpHash }).IsUnique();
            entity.HasIndex(dados => new { dados.ContaPsicologoId, dados.Status });
            entity.HasIndex(dados => dados.ContaPsicologoId)
                .IsUnique()
                .HasFilter("[Status] = 'Ativo'");

            entity.HasOne(dados => dados.Usuario)
                .WithOne(usuario => usuario.DadosProfissionaisPsicologo)
                .HasPrincipalKey<Usuario>(usuario => new { usuario.ContaPsicologoId, usuario.Id })
                .HasForeignKey<DadosProfissionaisPsicologo>(dados => new { dados.ContaPsicologoId, dados.UsuarioId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurarPacientes(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<Paciente>(modelBuilder);
        ConfigurarEntidadeDaConta<ContatoPaciente>(modelBuilder);

        modelBuilder.Entity<Paciente>(entity =>
        {
            entity.ToTable("Pacientes");
            entity.HasKey(paciente => paciente.Id);

            entity.Property(paciente => paciente.NomeCompleto).HasMaxLength(180).IsRequired();
            entity.Property(paciente => paciente.Cpf).HasMaxLength(500);
            entity.Property(paciente => paciente.CpfHash).HasMaxLength(128);
            entity.Property(paciente => paciente.Telefone).HasMaxLength(500);
            entity.Property(paciente => paciente.ValorSessao).HasPrecision(12, 2);
            entity.Property(paciente => paciente.Email).HasColumnType("nvarchar(max)");
            entity.Property(paciente => paciente.ObservacaoAdministrativa).HasColumnType("nvarchar(max)");

            entity.HasIndex(paciente => new { paciente.ContaPsicologoId, paciente.CpfHash })
                .IsUnique()
                .HasFilter("[CpfHash] IS NOT NULL");

            entity.HasIndex(paciente => new { paciente.ContaPsicologoId, paciente.UsuarioId })
                .IsUnique()
                .HasFilter("[UsuarioId] IS NOT NULL");

            entity.HasIndex(paciente => new { paciente.ContaPsicologoId, paciente.Status });

            entity.HasOne(paciente => paciente.Usuario)
                .WithOne(usuario => usuario.Paciente)
                .HasPrincipalKey<Usuario>(usuario => new { usuario.ContaPsicologoId, usuario.Id })
                .HasForeignKey<Paciente>(paciente => new { paciente.ContaPsicologoId, paciente.UsuarioId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ContatoPaciente>(entity =>
        {
            entity.ToTable("ContatosPaciente");
            entity.HasKey(contato => contato.Id);

            entity.Property(contato => contato.Valor).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(contato => contato.Nome).HasMaxLength(500);
            entity.Property(contato => contato.ValorHash).HasMaxLength(128).IsRequired();

            entity.HasIndex(contato => new { contato.ContaPsicologoId, contato.PacienteId, contato.TipoContato, contato.ValorHash });

            entity.HasOne(contato => contato.Paciente)
                .WithMany(paciente => paciente.Contatos)
                .HasPrincipalKey(paciente => new { paciente.ContaPsicologoId, paciente.Id })
                .HasForeignKey(contato => new { contato.ContaPsicologoId, contato.PacienteId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurarAgenda(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<ConfiguracaoAtendimento>(modelBuilder);
        ConfigurarEntidadeDaConta<TipoAtendimento>(modelBuilder);
        ConfigurarEntidadeDaConta<HorarioDisponivel>(modelBuilder);
        ConfigurarEntidadeDaConta<BloqueioAgenda>(modelBuilder);

        modelBuilder.Entity<ConfiguracaoAtendimento>(entity =>
        {
            entity.ToTable("ConfiguracoesAtendimento", table =>
            {
                table.HasCheckConstraint("CK_ConfiguracoesAtendimento_DuracaoPadraoMinutos", "[DuracaoPadraoMinutos] > 0");
            });

            entity.HasKey(configuracao => configuracao.Id);
            entity.Property(configuracao => configuracao.FusoHorario).HasMaxLength(80).IsRequired();
            entity.HasIndex(configuracao => new { configuracao.ContaPsicologoId, configuracao.Ativo })
                .IsUnique()
                .HasFilter("[Ativo] = 1");
        });

        modelBuilder.Entity<TipoAtendimento>(entity =>
        {
            entity.ToTable("TiposAtendimento", table =>
            {
                table.HasCheckConstraint("CK_TiposAtendimento_DuracaoMinutos", "[DuracaoMinutos] > 0");
                table.HasCheckConstraint("CK_TiposAtendimento_ValorPadrao", "[ValorPadrao] IS NULL OR [ValorPadrao] >= 0");
            });

            entity.HasKey(tipo => tipo.Id);

            entity.Property(tipo => tipo.Nome).HasMaxLength(120).IsRequired();
            entity.Property(tipo => tipo.Descricao).HasMaxLength(1000);
            entity.Property(tipo => tipo.ValorPadrao).HasPrecision(12, 2);

            entity.HasIndex(tipo => new { tipo.ContaPsicologoId, tipo.Nome }).IsUnique();
        });

        modelBuilder.Entity<HorarioDisponivel>(entity =>
        {
            entity.ToTable("HorariosDisponiveis", table =>
            {
                table.HasCheckConstraint("CK_HorariosDisponiveis_Periodo", "[HoraFim] > [HoraInicio]");
            });

            entity.HasKey(horario => horario.Id);
            entity.HasIndex(horario => new { horario.ContaPsicologoId, horario.DiaSemana, horario.HoraInicio, horario.HoraFim });
        });

        modelBuilder.Entity<BloqueioAgenda>(entity =>
        {
            entity.ToTable("BloqueiosAgenda", table =>
            {
                table.HasCheckConstraint("CK_BloqueiosAgenda_Periodo", "[FimEm] > [InicioEm]");
            });

            entity.HasKey(bloqueio => bloqueio.Id);

            entity.Property(bloqueio => bloqueio.Titulo).HasMaxLength(160).IsRequired();
            entity.Property(bloqueio => bloqueio.Descricao).HasMaxLength(1000);

            entity.HasIndex(bloqueio => new { bloqueio.ContaPsicologoId, bloqueio.InicioEm, bloqueio.FimEm });
        });
    }

    private static void ConfigurarConsultas(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<Consulta>(modelBuilder);
        ConfigurarEntidadeDaConta<HistoricoConsulta>(modelBuilder);

        modelBuilder.Entity<Consulta>(entity =>
        {
            entity.ToTable("Consultas", table =>
            {
                table.HasCheckConstraint("CK_Consultas_Periodo", "[FimEm] > [InicioEm]");
            });

            entity.HasKey(consulta => consulta.Id);

            entity.Property(consulta => consulta.LinkOnline).HasMaxLength(500);
            entity.Property(consulta => consulta.EnderecoAtendimento).HasMaxLength(500);
            entity.Property(consulta => consulta.ObservacaoAdministrativa).HasMaxLength(2000);
            entity.Property(consulta => consulta.JustificativaCancelamento).HasMaxLength(1000);
            entity.HasIndex(consulta => new { consulta.ContaPsicologoId, consulta.SerieId }).HasFilter("[SerieId] IS NOT NULL");

            entity.HasIndex(consulta => new { consulta.ContaPsicologoId, consulta.InicioEm, consulta.FimEm });
            entity.HasIndex(consulta => new { consulta.ContaPsicologoId, consulta.Status, consulta.InicioEm, consulta.FimEm });
            entity.HasIndex(consulta => new { consulta.ContaPsicologoId, consulta.InicioEm })
                .IsUnique()
                .HasFilter("[Status] IN ('AguardandoConfirmacao', 'Agendada', 'Confirmada', 'Realizada', 'NaoCompareceu')");

            entity.HasIndex(consulta => new { consulta.ContaPsicologoId, consulta.PacienteId, consulta.InicioEm });

            entity.HasOne(consulta => consulta.Paciente)
                .WithMany(paciente => paciente.Consultas)
                .HasPrincipalKey(paciente => new { paciente.ContaPsicologoId, paciente.Id })
                .HasForeignKey(consulta => new { consulta.ContaPsicologoId, consulta.PacienteId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(consulta => consulta.TipoAtendimento)
                .WithMany(tipo => tipo.Consultas)
                .HasPrincipalKey(tipo => new { tipo.ContaPsicologoId, tipo.Id })
                .HasForeignKey(consulta => new { consulta.ContaPsicologoId, consulta.TipoAtendimentoId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HistoricoConsulta>(entity =>
        {
            entity.ToTable("HistoricosConsulta");
            entity.HasKey(historico => historico.Id);

            entity.Property(historico => historico.Descricao).HasMaxLength(1000);

            entity.HasOne(historico => historico.Consulta)
                .WithMany(consulta => consulta.Historicos)
                .HasPrincipalKey(consulta => new { consulta.ContaPsicologoId, consulta.Id })
                .HasForeignKey(historico => new { historico.ContaPsicologoId, historico.ConsultaId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(historico => historico.Usuario)
                .WithMany(usuario => usuario.HistoricosConsulta)
                .HasPrincipalKey(usuario => new { usuario.ContaPsicologoId, usuario.Id })
                .HasForeignKey(historico => new { historico.ContaPsicologoId, historico.UsuarioId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurarProntuarios(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<Prontuario>(modelBuilder);
        ConfigurarEntidadeDaConta<EvolucaoProntuario>(modelBuilder);

        modelBuilder.Entity<Prontuario>(entity =>
        {
            entity.ToTable("Prontuarios");
            entity.HasKey(prontuario => prontuario.Id);

            entity.HasIndex(prontuario => new { prontuario.ContaPsicologoId, prontuario.PacienteId })
                .IsUnique()
                .HasFilter("[Status] = 'Ativo'");
            entity.HasIndex(prontuario => new { prontuario.ContaPsicologoId, prontuario.Status });

            entity.HasOne(prontuario => prontuario.Paciente)
                .WithMany(paciente => paciente.Prontuarios)
                .HasPrincipalKey(paciente => new { paciente.ContaPsicologoId, paciente.Id })
                .HasForeignKey(prontuario => new { prontuario.ContaPsicologoId, prontuario.PacienteId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EvolucaoProntuario>(entity =>
        {
            entity.ToTable("EvolucoesProntuario");
            entity.HasKey(evolucao => evolucao.Id);

            entity.Property(evolucao => evolucao.Conteudo).HasColumnType("nvarchar(max)").IsRequired();

            entity.HasIndex(evolucao => new { evolucao.ContaPsicologoId, evolucao.PacienteId, evolucao.DataEvolucao });
            entity.HasIndex(evolucao => new { evolucao.ContaPsicologoId, evolucao.ProntuarioId, evolucao.DataEvolucao });

            entity.HasOne(evolucao => evolucao.Prontuario)
                .WithMany(prontuario => prontuario.Evolucoes)
                .HasPrincipalKey(prontuario => new { prontuario.ContaPsicologoId, prontuario.PacienteId, prontuario.Id })
                .HasForeignKey(evolucao => new { evolucao.ContaPsicologoId, evolucao.PacienteId, evolucao.ProntuarioId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(evolucao => evolucao.Paciente)
                .WithMany()
                .HasPrincipalKey(paciente => new { paciente.ContaPsicologoId, paciente.Id })
                .HasForeignKey(evolucao => new { evolucao.ContaPsicologoId, evolucao.PacienteId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(evolucao => evolucao.RegistroSessao)
                .WithMany(registro => registro.Evolucoes)
                .HasPrincipalKey(registro => new { registro.ContaPsicologoId, registro.PacienteId, registro.ProntuarioId, registro.Id })
                .HasForeignKey(evolucao => new { evolucao.ContaPsicologoId, evolucao.PacienteId, evolucao.ProntuarioId, evolucao.RegistroSessaoId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(evolucao => evolucao.CriadoPorUsuario)
                .WithMany()
                .HasPrincipalKey(usuario => new { usuario.ContaPsicologoId, usuario.Id })
                .HasForeignKey(evolucao => new { evolucao.ContaPsicologoId, evolucao.CriadoPorUsuarioId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurarDocumentosPsicologicos(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<DocumentoProntuario>(modelBuilder);
        ConfigurarEntidadeDaConta<VersaoDocumentoProntuario>(modelBuilder);
        ConfigurarEntidadeDaConta<ModeloDocumentoPsicologico>(modelBuilder);
        ConfigurarEntidadeDaConta<ProtocoloEntregaDocumento>(modelBuilder);

        modelBuilder.Entity<DocumentoProntuario>(entity =>
        {
            entity.ToTable("DocumentosProntuario");
            entity.HasKey(documento => documento.Id);

            entity.Property(documento => documento.Titulo).HasMaxLength(180).IsRequired();
            entity.Property(documento => documento.Finalidade).HasMaxLength(1000);
            entity.Property(documento => documento.Solicitante).HasMaxLength(180);
            entity.Property(documento => documento.ConteudoAtualJson).HasColumnType("nvarchar(max)");
            entity.Property(documento => documento.HashConteudoFinal).HasMaxLength(128);

            entity.HasIndex(documento => new { documento.ContaPsicologoId, documento.PacienteId, documento.CriadoEm });
            entity.HasIndex(documento => new { documento.ContaPsicologoId, documento.ProntuarioId, documento.Tipo, documento.Status });
            entity.HasIndex(documento => new { documento.ContaPsicologoId, documento.Status, documento.FinalizadoEm });

            entity.HasOne(documento => documento.Prontuario)
                .WithMany(prontuario => prontuario.Documentos)
                .HasPrincipalKey(prontuario => new { prontuario.ContaPsicologoId, prontuario.PacienteId, prontuario.Id })
                .HasForeignKey(documento => new { documento.ContaPsicologoId, documento.PacienteId, documento.ProntuarioId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(documento => documento.Paciente)
                .WithMany()
                .HasPrincipalKey(paciente => new { paciente.ContaPsicologoId, paciente.Id })
                .HasForeignKey(documento => new { documento.ContaPsicologoId, documento.PacienteId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(documento => documento.CriadoPorUsuario)
                .WithMany()
                .HasPrincipalKey(usuario => new { usuario.ContaPsicologoId, usuario.Id })
                .HasForeignKey(documento => new { documento.ContaPsicologoId, documento.CriadoPorUsuarioId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(documento => documento.FinalizadoPorUsuario)
                .WithMany()
                .HasPrincipalKey(usuario => new { usuario.ContaPsicologoId, usuario.Id })
                .HasForeignKey(documento => new { documento.ContaPsicologoId, documento.FinalizadoPorUsuarioId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VersaoDocumentoProntuario>(entity =>
        {
            entity.ToTable("VersoesDocumentoProntuario", table =>
            {
                table.HasCheckConstraint("CK_VersoesDocumentoProntuario_NumeroVersao", "[NumeroVersao] > 0");
            });

            entity.HasKey(versao => versao.Id);

            entity.Property(versao => versao.ConteudoJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(versao => versao.MotivoAlteracao).HasMaxLength(1000);
            entity.Property(versao => versao.HashConteudo).HasMaxLength(128).IsRequired();

            entity.HasIndex(versao => new { versao.ContaPsicologoId, versao.DocumentoProntuarioId, versao.NumeroVersao }).IsUnique();
            entity.HasIndex(versao => new { versao.ContaPsicologoId, versao.CriadoEm });

            entity.HasOne(versao => versao.DocumentoProntuario)
                .WithMany(documento => documento.Versoes)
                .HasPrincipalKey(documento => new { documento.ContaPsicologoId, documento.Id })
                .HasForeignKey(versao => new { versao.ContaPsicologoId, versao.DocumentoProntuarioId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(versao => versao.CriadoPorUsuario)
                .WithMany()
                .HasPrincipalKey(usuario => new { usuario.ContaPsicologoId, usuario.Id })
                .HasForeignKey(versao => new { versao.ContaPsicologoId, versao.CriadoPorUsuarioId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ModeloDocumentoPsicologico>(entity =>
        {
            entity.ToTable("ModelosDocumentoPsicologico");
            entity.HasKey(modelo => modelo.Id);

            entity.Property(modelo => modelo.Nome).HasMaxLength(160).IsRequired();
            entity.Property(modelo => modelo.EstruturaJson).HasMaxLength(12000).IsRequired();

            entity.HasIndex(modelo => new { modelo.ContaPsicologoId, modelo.Tipo, modelo.Nome }).IsUnique();
            entity.HasIndex(modelo => new { modelo.ContaPsicologoId, modelo.Tipo, modelo.Ativo });
        });

        modelBuilder.Entity<RegraDocumentoPsicologico>(entity =>
        {
            entity.ToTable("RegrasDocumentoPsicologico");
            entity.HasKey(regra => regra.Id);

            entity.Property(regra => regra.Nome).HasMaxLength(160).IsRequired();
            entity.Property(regra => regra.Descricao).HasMaxLength(2000).IsRequired();
            entity.Property(regra => regra.RegraJson).HasMaxLength(12000).IsRequired();

            entity.HasIndex(regra => new { regra.Tipo, regra.Nome }).IsUnique();
            entity.HasIndex(regra => new { regra.Tipo, regra.Ativa });
        });

        modelBuilder.Entity<ProtocoloEntregaDocumento>(entity =>
        {
            entity.ToTable("ProtocolosEntregaDocumento");
            entity.HasKey(protocolo => protocolo.Id);

            entity.Property(protocolo => protocolo.Destinatario).HasMaxLength(180);
            entity.Property(protocolo => protocolo.Observacao).HasMaxLength(1000);

            entity.HasIndex(protocolo => new { protocolo.ContaPsicologoId, protocolo.DocumentoProntuarioId, protocolo.EntregueEm });
            entity.HasIndex(protocolo => new { protocolo.ContaPsicologoId, protocolo.Status, protocolo.EntregueEm });

            entity.HasOne(protocolo => protocolo.DocumentoProntuario)
                .WithMany(documento => documento.ProtocolosEntrega)
                .HasPrincipalKey(documento => new { documento.ContaPsicologoId, documento.Id })
                .HasForeignKey(protocolo => new { protocolo.ContaPsicologoId, protocolo.DocumentoProntuarioId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(protocolo => protocolo.RegistradoPorUsuario)
                .WithMany()
                .HasPrincipalKey(usuario => new { usuario.ContaPsicologoId, usuario.Id })
                .HasForeignKey(protocolo => new { protocolo.ContaPsicologoId, protocolo.RegistradoPorUsuarioId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(protocolo => protocolo.VersaoDocumentoProntuario)
                .WithMany()
                .HasPrincipalKey(versao => new { versao.ContaPsicologoId, versao.DocumentoProntuarioId, versao.Id })
                .HasForeignKey(protocolo => new { protocolo.ContaPsicologoId, protocolo.DocumentoProntuarioId, protocolo.VersaoDocumentoProntuarioId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurarSessoes(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<RegistroSessao>(modelBuilder);
        ConfigurarEntidadeDaConta<RetificacaoRegistroSessao>(modelBuilder);

        modelBuilder.Entity<RegistroSessao>(entity =>
        {
            entity.ToTable("RegistrosSessao");
            entity.HasKey(registro => registro.Id);

            entity.Property(registro => registro.ObservacoesClinicas).HasColumnType("nvarchar(max)");
            entity.Property(registro => registro.Evolucao).HasColumnType("nvarchar(max)");
            entity.Property(registro => registro.Encaminhamentos).HasColumnType("nvarchar(max)");

            entity.HasIndex(registro => new { registro.ContaPsicologoId, registro.ConsultaId }).IsUnique();
            entity.HasIndex(registro => new { registro.ContaPsicologoId, registro.PacienteId, registro.DataSessao });
            entity.HasIndex(registro => new { registro.ContaPsicologoId, registro.ProntuarioId, registro.DataSessao });

            entity.HasOne(registro => registro.Prontuario)
                .WithMany(prontuario => prontuario.RegistrosSessao)
                .HasPrincipalKey(prontuario => new { prontuario.ContaPsicologoId, prontuario.PacienteId, prontuario.Id })
                .HasForeignKey(registro => new { registro.ContaPsicologoId, registro.PacienteId, registro.ProntuarioId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(registro => registro.Paciente)
                .WithMany(paciente => paciente.RegistrosSessao)
                .HasPrincipalKey(paciente => new { paciente.ContaPsicologoId, paciente.Id })
                .HasForeignKey(registro => new { registro.ContaPsicologoId, registro.PacienteId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(registro => registro.Consulta)
                .WithOne(consulta => consulta.RegistroSessao)
                .HasPrincipalKey<Consulta>(consulta => new { consulta.ContaPsicologoId, consulta.PacienteId, consulta.TipoAtendimentoId, consulta.Id })
                .HasForeignKey<RegistroSessao>(registro => new { registro.ContaPsicologoId, registro.PacienteId, registro.TipoAtendimentoId, registro.ConsultaId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(registro => registro.TipoAtendimento)
                .WithMany(tipo => tipo.RegistrosSessao)
                .HasPrincipalKey(tipo => new { tipo.ContaPsicologoId, tipo.Id })
                .HasForeignKey(registro => new { registro.ContaPsicologoId, registro.TipoAtendimentoId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(registro => registro.CriadoPorUsuario)
                .WithMany(usuario => usuario.RegistrosSessaoCriados)
                .HasPrincipalKey(usuario => new { usuario.ContaPsicologoId, usuario.Id })
                .HasForeignKey(registro => new { registro.ContaPsicologoId, registro.CriadoPorUsuarioId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RetificacaoRegistroSessao>(entity =>
        {
            entity.ToTable("RetificacoesRegistroSessao");
            entity.HasKey(retificacao => retificacao.Id);

            entity.Property(retificacao => retificacao.CampoAlterado).HasMaxLength(120).IsRequired();
            entity.Property(retificacao => retificacao.ValorAnterior).HasColumnType("nvarchar(max)");
            entity.Property(retificacao => retificacao.ValorNovo).HasColumnType("nvarchar(max)");
            entity.Property(retificacao => retificacao.Motivo).HasMaxLength(1000);

            entity.HasOne(retificacao => retificacao.RegistroSessao)
                .WithMany(registro => registro.Retificacoes)
                .HasPrincipalKey(registro => new { registro.ContaPsicologoId, registro.Id })
                .HasForeignKey(retificacao => new { retificacao.ContaPsicologoId, retificacao.RegistroSessaoId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(retificacao => retificacao.Usuario)
                .WithMany(usuario => usuario.RetificacoesRegistroSessao)
                .HasPrincipalKey(usuario => new { usuario.ContaPsicologoId, usuario.Id })
                .HasForeignKey(retificacao => new { retificacao.ContaPsicologoId, retificacao.UsuarioId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurarPagamentos(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<Pagamento>(modelBuilder);

        modelBuilder.Entity<Pagamento>(entity =>
        {
            entity.ToTable("Pagamentos", table =>
            {
                table.HasCheckConstraint("CK_Pagamentos_Valor", "[Valor] >= 0");
            });

            entity.HasKey(pagamento => pagamento.Id);

            entity.Property(pagamento => pagamento.Valor).HasPrecision(12, 2);
            entity.Property(pagamento => pagamento.Status).IsConcurrencyToken();
            entity.HasIndex(pagamento => new { pagamento.ContaPsicologoId, pagamento.PacienteId, pagamento.Competencia }).HasFilter("[Competencia] IS NOT NULL");
            entity.Property(pagamento => pagamento.Observacao).HasMaxLength(1000);

            entity.HasIndex(pagamento => new { pagamento.ContaPsicologoId, pagamento.PacienteId, pagamento.Status });
            entity.HasIndex(pagamento => new { pagamento.ContaPsicologoId, pagamento.ConsultaId });

            entity.HasOne(pagamento => pagamento.Paciente)
                .WithMany(paciente => paciente.Pagamentos)
                .HasPrincipalKey(paciente => new { paciente.ContaPsicologoId, paciente.Id })
                .HasForeignKey(pagamento => new { pagamento.ContaPsicologoId, pagamento.PacienteId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(pagamento => pagamento.Consulta)
                .WithMany(consulta => consulta.Pagamentos)
                .HasPrincipalKey(consulta => new { consulta.ContaPsicologoId, consulta.PacienteId, consulta.Id })
                .HasForeignKey(pagamento => new { pagamento.ContaPsicologoId, pagamento.PacienteId, pagamento.ConsultaId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurarLembretes(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<Lembrete>(modelBuilder);
        ConfigurarEntidadeDaConta<ModeloMensagem>(modelBuilder);
        ConfigurarEntidadeDaConta<ListaEspera>(modelBuilder);

        modelBuilder.Entity<Lembrete>(entity =>
        {
            entity.ToTable("Lembretes");
            entity.HasKey(lembrete => lembrete.Id);

            entity.Property(lembrete => lembrete.Assunto).HasMaxLength(200);
            entity.Property(lembrete => lembrete.Mensagem).HasMaxLength(2000);
            entity.Property(lembrete => lembrete.ErroEnvio).HasMaxLength(2000);

            entity.HasIndex(lembrete => new { lembrete.ContaPsicologoId, lembrete.AgendadoPara, lembrete.Status });

            entity.HasOne(lembrete => lembrete.Consulta)
                .WithMany(consulta => consulta.Lembretes)
                .HasPrincipalKey(consulta => new { consulta.ContaPsicologoId, consulta.PacienteId, consulta.Id })
                .HasForeignKey(lembrete => new { lembrete.ContaPsicologoId, lembrete.PacienteId, lembrete.ConsultaId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(lembrete => lembrete.Paciente)
                .WithMany()
                .HasPrincipalKey(paciente => new { paciente.ContaPsicologoId, paciente.Id })
                .HasForeignKey(lembrete => new { lembrete.ContaPsicologoId, lembrete.PacienteId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(lembrete => lembrete.ModeloMensagem)
                .WithMany(modelo => modelo.Lembretes)
                .HasPrincipalKey(modelo => new { modelo.ContaPsicologoId, modelo.Id })
                .HasForeignKey(lembrete => new { lembrete.ContaPsicologoId, lembrete.ModeloMensagemId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ModeloMensagem>(entity =>
        {
            entity.ToTable("ModelosMensagem");
            entity.HasKey(modelo => modelo.Id);

            entity.Property(modelo => modelo.Nome).HasMaxLength(120).IsRequired();
            entity.Property(modelo => modelo.Assunto).HasMaxLength(200);
            entity.Property(modelo => modelo.Conteudo).HasMaxLength(4000).IsRequired();

            entity.HasIndex(modelo => new { modelo.ContaPsicologoId, modelo.Tipo, modelo.Canal, modelo.Ativo });
        });

        modelBuilder.Entity<ListaEspera>(entity =>
        {
            entity.ToTable("ListaEspera", table =>
            {
                table.HasCheckConstraint(
                    "CK_ListaEspera_PreferenciaHorario",
                    "[PreferenciaHorarioInicio] IS NULL OR [PreferenciaHorarioFim] IS NULL OR [PreferenciaHorarioFim] > [PreferenciaHorarioInicio]");
            });

            entity.HasKey(item => item.Id);

            entity.Property(item => item.ObservacaoAdministrativa).HasMaxLength(1000);

            entity.HasIndex(item => new { item.ContaPsicologoId, item.Status, item.CriadoEm });

            entity.HasOne(item => item.Paciente)
                .WithMany(paciente => paciente.ListaEspera)
                .HasPrincipalKey(paciente => new { paciente.ContaPsicologoId, paciente.Id })
                .HasForeignKey(item => new { item.ContaPsicologoId, item.PacienteId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(item => item.TipoAtendimento)
                .WithMany(tipo => tipo.ListaEspera)
                .HasPrincipalKey(tipo => new { tipo.ContaPsicologoId, tipo.Id })
                .HasForeignKey(item => new { item.ContaPsicologoId, item.TipoAtendimentoId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurarWhatsApp(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<IntegracaoWhatsApp>(modelBuilder);
        ConfigurarEntidadeDaConta<ConversaWhatsApp>(modelBuilder);
        ConfigurarEntidadeDaConta<MensagemWhatsApp>(modelBuilder);
        ConfigurarEntidadeDaConta<EventoBridgeWhatsApp>(modelBuilder);

        modelBuilder.Entity<IntegracaoWhatsApp>(entity =>
        {
            entity.ToTable("IntegracoesWhatsApp");
            entity.HasKey(integracao => integracao.Id);

            entity.Property(integracao => integracao.Nome).HasMaxLength(120).IsRequired();
            entity.Property(integracao => integracao.Provedor).HasMaxLength(120).IsRequired();
            entity.Property(integracao => integracao.UrlBaseBridge).HasMaxLength(500).IsRequired();
            entity.Property(integracao => integracao.TokenCriptografado).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(integracao => integracao.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(integracao => integracao.NumeroWhatsApp).HasMaxLength(30).IsRequired();
            entity.Property(integracao => integracao.UltimoErro).HasMaxLength(2000);

            entity.HasIndex(integracao => new { integracao.ContaPsicologoId, integracao.Ativo });
            entity.HasIndex(integracao => integracao.TokenHash).IsUnique();
        });

        modelBuilder.Entity<ConversaWhatsApp>(entity =>
        {
            entity.ToTable("ConversasWhatsApp");
            entity.HasKey(conversa => conversa.Id);

            entity.Property(conversa => conversa.Telefone).HasMaxLength(500).IsRequired();
            entity.Property(conversa => conversa.NomeContato).HasMaxLength(180);
            entity.Property(conversa => conversa.MotivoAtendimentoHumano).HasMaxLength(1000);

            entity.Property(conversa => conversa.TelefoneHash).HasMaxLength(64);
            entity.HasIndex(conversa => new { conversa.ContaPsicologoId, conversa.IntegracaoWhatsAppId, conversa.TelefoneHash });
            entity.HasIndex(conversa => new { conversa.ContaPsicologoId, conversa.Status, conversa.UltimaMensagemEm });

            entity.HasOne(conversa => conversa.IntegracaoWhatsApp)
                .WithMany(integracao => integracao.Conversas)
                .HasPrincipalKey(integracao => new { integracao.ContaPsicologoId, integracao.Id })
                .HasForeignKey(conversa => new { conversa.ContaPsicologoId, conversa.IntegracaoWhatsAppId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(conversa => conversa.Paciente)
                .WithMany(paciente => paciente.ConversasWhatsApp)
                .HasPrincipalKey(paciente => new { paciente.ContaPsicologoId, paciente.Id })
                .HasForeignKey(conversa => new { conversa.ContaPsicologoId, conversa.PacienteId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MensagemWhatsApp>(entity =>
        {
            entity.ToTable("MensagensWhatsApp");
            entity.HasKey(mensagem => mensagem.Id);

            entity.Property(mensagem => mensagem.MensagemExternaId).HasMaxLength(180);
            entity.HasIndex(mensagem => new { mensagem.ContaPsicologoId, mensagem.LembreteId })
                .IsUnique().HasFilter("[LembreteId] IS NOT NULL");
            entity.HasOne<Lembrete>().WithMany()
                .HasPrincipalKey(lembrete => new { lembrete.ContaPsicologoId, lembrete.Id })
                .HasForeignKey(mensagem => new { mensagem.ContaPsicologoId, mensagem.LembreteId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(mensagem => mensagem.Conteudo).HasColumnType("nvarchar(max)");
            entity.Property(mensagem => mensagem.ErroEnvio).HasMaxLength(2000);

            entity.HasIndex(mensagem => new { mensagem.ContaPsicologoId, mensagem.ConversaWhatsAppId, mensagem.MensagemExternaId })
                .IsUnique()
                .HasFilter("[MensagemExternaId] IS NOT NULL");

            entity.HasOne(mensagem => mensagem.ConversaWhatsApp)
                .WithMany(conversa => conversa.Mensagens)
                .HasPrincipalKey(conversa => new { conversa.ContaPsicologoId, conversa.Id })
                .HasForeignKey(mensagem => new { mensagem.ContaPsicologoId, mensagem.ConversaWhatsAppId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EventoBridgeWhatsApp>(entity =>
        {
            entity.ToTable("EventosBridgeWhatsApp");
            entity.HasKey(evento => evento.Id);

            entity.Property(evento => evento.EventoExternoId).HasMaxLength(180);
            entity.Property(evento => evento.TipoEvento).HasMaxLength(120).IsRequired();
            entity.Property(evento => evento.MensagemExternaId).HasMaxLength(180);
            entity.HasIndex(evento => new { evento.ContaPsicologoId, evento.MensagemExternaId });
            entity.Property(evento => evento.PayloadHash).HasMaxLength(128);
            entity.Property(evento => evento.PayloadJson).IsRequired();
            entity.Property(evento => evento.ErroProcessamento).HasMaxLength(2000);

            entity.HasIndex(evento => new { evento.ContaPsicologoId, evento.IntegracaoWhatsAppId, evento.EventoExternoId })
                .IsUnique()
                .HasFilter("[EventoExternoId] IS NOT NULL");

            entity.HasIndex(evento => new { evento.ContaPsicologoId, evento.IntegracaoWhatsAppId, evento.PayloadHash })
                .IsUnique()
                .HasFilter("[PayloadHash] IS NOT NULL");

            entity.HasOne(evento => evento.IntegracaoWhatsApp)
                .WithMany(integracao => integracao.Eventos)
                .HasPrincipalKey(integracao => new { integracao.ContaPsicologoId, integracao.Id })
                .HasForeignKey(evento => new { evento.ContaPsicologoId, evento.IntegracaoWhatsAppId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurarBot(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<SessaoBot>(modelBuilder);

        modelBuilder.Entity<SessaoBot>(entity =>
        {
            entity.ToTable("SessoesBot");
            entity.HasKey(sessao => sessao.Id);

            entity.Property(sessao => sessao.IntencaoAtual).HasMaxLength(120).IsRequired();
            entity.Property(sessao => sessao.EtapaAtual).HasMaxLength(120);
            entity.Property(sessao => sessao.DadosTemporariosJson).HasColumnType("nvarchar(max)");

            entity.HasIndex(sessao => new { sessao.ContaPsicologoId, sessao.ConversaWhatsAppId, sessao.Status });
            entity.HasIndex(sessao => new { sessao.ContaPsicologoId, sessao.ConversaWhatsAppId })
                .IsUnique()
                .HasFilter("[Status] = 'Aberta'");

            entity.HasOne(sessao => sessao.ConversaWhatsApp)
                .WithMany(conversa => conversa.SessoesBot)
                .HasPrincipalKey(conversa => new { conversa.ContaPsicologoId, conversa.Id })
                .HasForeignKey(sessao => new { sessao.ContaPsicologoId, sessao.ConversaWhatsAppId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(sessao => sessao.Paciente)
                .WithMany(paciente => paciente.SessoesBot)
                .HasPrincipalKey(paciente => new { paciente.ContaPsicologoId, paciente.Id })
                .HasForeignKey(sessao => new { sessao.ContaPsicologoId, sessao.PacienteId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IntencaoBot>(entity =>
        {
            entity.ToTable("IntencoesBot");
            entity.HasKey(intencao => intencao.Id);

            entity.Property(intencao => intencao.Nome).HasMaxLength(120).IsRequired();
            entity.Property(intencao => intencao.Descricao).HasMaxLength(1000);
            entity.HasIndex(intencao => intencao.Nome).IsUnique();
        });
    }

    private static void ConfigurarAuditoria(ModelBuilder modelBuilder)
    {
        ConfigurarEntidadeDaConta<LogAuditoria>(modelBuilder);
        ConfigurarEntidadeDaConta<Notificacao>(modelBuilder);

        modelBuilder.Entity<LogAuditoria>(entity =>
        {
            entity.ToTable("LogsAuditoria");
            entity.HasKey(log => log.Id);

            entity.Property(log => log.TipoAcao).HasMaxLength(120).IsRequired();
            entity.Property(log => log.Entidade).HasMaxLength(120).IsRequired();
            entity.Property(log => log.Descricao).HasMaxLength(2000);
            entity.Property(log => log.Ip).HasMaxLength(80);
            entity.Property(log => log.UserAgent).HasMaxLength(500);
            entity.Property(log => log.DadosAntesJson).HasColumnType("nvarchar(max)");
            entity.Property(log => log.DadosDepoisJson).HasColumnType("nvarchar(max)");

            entity.HasIndex(log => new { log.ContaPsicologoId, log.Entidade, log.EntidadeId });
            entity.HasIndex(log => new { log.ContaPsicologoId, log.CriadoEm });

            entity.HasOne(log => log.Usuario)
                .WithMany(usuario => usuario.LogsAuditoria)
                .HasPrincipalKey(usuario => new { usuario.ContaPsicologoId, usuario.Id })
                .HasForeignKey(log => new { log.ContaPsicologoId, log.UsuarioId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Notificacao>(entity =>
        {
            entity.ToTable("Notificacoes");
            entity.HasKey(notificacao => notificacao.Id);

            entity.Property(notificacao => notificacao.Titulo).HasMaxLength(160).IsRequired();
            entity.Property(notificacao => notificacao.Mensagem).HasMaxLength(2000).IsRequired();
            entity.Property(notificacao => notificacao.Tipo).HasMaxLength(120).IsRequired();

            entity.HasIndex(notificacao => new { notificacao.ContaPsicologoId, notificacao.UsuarioId, notificacao.Lida, notificacao.CriadoEm });

            entity.HasOne(notificacao => notificacao.Usuario)
                .WithMany(usuario => usuario.Notificacoes)
                .HasPrincipalKey(usuario => new { usuario.ContaPsicologoId, usuario.Id })
                .HasForeignKey(notificacao => new { notificacao.ContaPsicologoId, notificacao.UsuarioId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
