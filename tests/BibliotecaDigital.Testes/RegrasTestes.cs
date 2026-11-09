using System.Text;
using BibliotecaDigital.Acervo;
using BibliotecaDigital.Busca;
using BibliotecaDigital.Extracao;

namespace BibliotecaDigital.Testes;

public class FormatoTestes
{
    [Fact]
    public void Ida_e_volta_preserva_cada_caractere_mesmo_com_sintaxe_de_markdown_no_ocr()
    {
        var doc = new DocumentoTexto
        {
            Titulo = "Revista Exemplo — Tomo I",
            Cabecalho = [new("formato", FormatoMarkdown.Versao), new("titulo", "Título com \"aspas\" e : dois-pontos")],
            Paginas =
            [
                new PaginaTexto
                {
                    Numero = 10, Impressa = "8", OrigemImpressa = OrigemImpressa.Detectada, Colunas = 2,
                    HifensRemovidos = 3, MeiaLargura = 41, NaoLidos = 2,
                    Margens = ["-8-", "seta --> no meio", "barra \\ invertida"],
                    Paragrafos =
                    [
                        new Paragrafo("# não é título ~~nem tachado~~ *nem itálico* <b>nem html</b> [nem link]"),
                        new Paragrafo("1. não é lista e 2) também não"),
                        new Paragrafo("- nem item, = nem sublinhado, + nem mais | nem tabela & nem entidade"),
                        new Paragrafo("<!-- p. 3 --> não é marcador de página", 2),
                        new Paragrafo("barra \\ invertida e \\# já escapada no original", 2),
                    ],
                },
                new PaginaTexto { Numero = 11, Fidelidade = Fidelidade.SemTexto },
            ],
        };
        var lido = FormatoMarkdown.Ler(FormatoMarkdown.Gravar(doc));

        Assert.Equal(doc.Titulo, lido.Titulo);
        Assert.Equal(doc.Cabecalho, lido.Cabecalho);
        Assert.Equal(2, lido.Paginas.Count);
        var p = lido.Paginas[0];
        Assert.Equal(("8", OrigemImpressa.Detectada, 2, 3, 41, 2), (p.Impressa, p.OrigemImpressa!.Value, p.Colunas, p.HifensRemovidos, p.MeiaLargura, p.NaoLidos));
        Assert.Equal(doc.Paginas[0].Margens, p.Margens);
        Assert.Equal(doc.Paginas[0].Paragrafos, p.Paragrafos);
        Assert.Equal(Fidelidade.SemTexto, lido.Paginas[1].Fidelidade);
    }

    [Fact]
    public void Marcador_de_pagina_e_um_comentario_html_em_linha_propria()
    {
        var md = FormatoMarkdown.Gravar(new DocumentoTexto { Titulo = "x", Cabecalho = [], Paginas = [new PaginaTexto { Numero = 7 }] });
        Assert.Matches(@"(?m)^<!-- p\. 7 -->\s*$", md); // ferramentas externas de citação acham a página por este padrão
    }
}

public class TextoTestes
{
    [Fact]
    public void Meia_largura_volta_a_ser_a_letra_gravada()
    {
        Assert.Equal("éJbaste~eram ", Texto.DesfazerMeiaLargura("￩ｊ｢｡ｳｴ･ｾ･ｲ｡ｭ＠", out var n));
        Assert.Equal(13, n);
        Assert.Equal("~3-", Texto.DesfazerMeiaLargura("ｾＳＭ", out _));
        Assert.Equal("–", Texto.DesfazerMeiaLargura("ﾖ", out _)); // 0x96 em Windows-1252
        Assert.Equal("Arquivo", Texto.DesfazerMeiaLargura("Arquivo", out var zero));
        Assert.Equal(0, zero);
    }

    [Fact]
    public void Slug_e_estavel_e_sem_acento() =>
        Assert.Equal("tomo-xxix-n-01-marco", Texto.Slug("Tomo XXIX - n.01 Março"));

    [Fact]
    public void Multiconjunto_ignora_espaco() =>
        Assert.Equal(Texto.Multiconjunto(["a b", "c"]), Texto.Multiconjunto(["abc"]));
}

public class HifenizacaoTestes
{
    private static string Juntar(string a, string b, Lexico? lexico = null)
    {
        var sb = new StringBuilder(a);
        MontadorDePagina.Juntar(sb, b, [], lexico);
        return sb.ToString();
    }

    private static Lexico Lex(params string[] palavras) =>
        Lexico.DeContagem(palavras.ToDictionary(p => p, _ => 5));

    [Fact]
    public void Hifenizacao_sai_e_palavra_composta_fica()
    {
        var lex = Lex("parte", "tornou", "se", "segunda");
        Assert.Equal("da parte que", Juntar("da par-", "te que", lex));
        Assert.Equal("tornou-se nos", Juntar("tornou-", "se nos", lex));
        Assert.Equal("segunda-feira", Juntar("segunda-", "feira", lex));
        Assert.Equal("1921-1923", Juntar("1921-", "1923"));
        Assert.Equal("da Ale-Manha", Juntar("da Ale-", "Manha"));
    }

    [Fact]
    public void Sem_lexico_o_hifen_de_fim_de_linha_sai() =>
        Assert.Equal("progresso da região", Juntar("progresso da re-", "gião"));

    [Fact]
    public void Ponto_medio_lido_no_lugar_do_hifen_tambem_sai() =>
        Assert.Equal("da região dos", Juntar("da re ·", "gião dos"));

    [Fact]
    public void Travessao_solto_nao_e_hifen() =>
        Assert.Equal("diz - e segue", Juntar("diz -", "e segue"));

    [Fact]
    public void Remocao_e_declarada_para_a_conferencia()
    {
        var removidos = new List<string>();
        MontadorDePagina.Juntar(new StringBuilder("par-"), "te", removidos);
        Assert.Equal(["-"], removidos);
    }

    [Fact]
    public void So_espaco_e_normalizado() =>
        Assert.Equal("dos índios. Fim (texto)", MontadorDePagina.Espacos("  dos   índios . Fim ( texto )"));
}

public class OrdemDeLeituraTestes
{
    private static Palavra P(string t, double x, double y, double largura = 30) => new(t, new Caixa(x, y, x + largura, y + 10));

    [Fact]
    public void Duas_colunas_leem_a_primeira_inteira_antes_da_segunda()
    {
        var palavras = new List<Palavra>();
        for (var i = 0; i < 6; i++)
        {
            palavras.Add(P($"esq{i}", 40, 100 + i * 14));
            palavras.Add(P($"dir{i}", 260, 100 + i * 14));
        }
        var pagina = new PaginaLayout(1, 480, 720, [new Bloco(0, [new Linha(palavras, new Caixa(40, 100, 290, 184))], new Caixa(40, 100, 290, 184))]);
        var ordem = OrdemDeLeitura.Blocos(pagina).SelectMany(b => b.Linhas).SelectMany(l => l.Palavras).Select(p => p.Texto).ToList();
        Assert.Equal(["esq0", "esq1", "esq2", "esq3", "esq4", "esq5", "dir0", "dir1", "dir2", "dir3", "dir4", "dir5"], ordem);
    }

    [Fact]
    public void Espaco_largo_de_justificacao_nao_vira_coluna()
    {
        // Linhas justificadas com vão largo em lugares diferentes: é uma coluna só.
        var palavras = new List<Palavra>
        {
            P("a1", 40, 100, 30), P("a2", 160, 100),
            P("b1", 40, 114, 60), P("b2", 110, 114, 80),
            P("c1", 40, 128, 45), P("c2", 95, 128, 95),
        };
        var pagina = new PaginaLayout(1, 480, 720, [new Bloco(0, [new Linha(palavras, new Caixa(40, 100, 190, 138))], new Caixa(40, 100, 190, 138))]);
        var ordem = OrdemDeLeitura.Blocos(pagina).SelectMany(b => b.Linhas).Select(l => l.Texto).ToList();
        Assert.Equal(["a1 a2", "b1 b2", "c1 c2"], ordem);
    }

    [Fact]
    public void Numero_de_pagina_e_carimbo_atravessando_a_calha_nao_impedem_as_colunas()
    {
        // 1988, p. 10: o "-8-" e o carimbo centralizados no pé atravessam a calha entre as colunas.
        var palavras = new List<Palavra>();
        for (var i = 0; i < 8; i++)
        {
            palavras.Add(P($"esq{i}", 40, 100 + i * 14, 180));
            palavras.Add(P($"dir{i}", 250, 100 + i * 14, 180));
        }
        palavras.Add(P("-8-", 215, 220, 40));
        palavras.Add(P("Digitalizado", 110, 240, 260));
        var pagina = new PaginaLayout(1, 480, 720, [new Bloco(0, [new Linha(palavras, new Caixa(40, 100, 430, 250))], new Caixa(40, 100, 430, 250))]);
        var ordem = OrdemDeLeitura.Blocos(pagina).SelectMany(b => b.Linhas).SelectMany(l => l.Palavras).Select(p => p.Texto).ToList();
        Assert.Equal(["esq0", "esq1", "esq2", "esq3", "esq4", "esq5", "esq6", "esq7", "dir0", "dir1", "dir2", "dir3", "dir4", "dir5", "dir6", "dir7", "-8-", "Digitalizado"], ordem);
    }

    [Fact]
    public void Palavra_que_o_poppler_partiu_volta_a_ser_uma()
    {
        // 1960, p. 3: "T|i|vemos" com vão de 0,08–0,10 da altura da letra.
        var unidas = OrdemDeLeitura.Unir([P("T", 100, 0, 6), P("i", 107, 0, 3), P("vemos", 110.8, 0, 28), P("que", 143, 0, 18)]);
        Assert.Equal(["Tivemos", "que"], unidas.Select(p => p.Texto));
    }
}

public class PaginacaoTestes
{
    [Fact]
    public void Detecta_confirma_pelo_vizinho_e_interpola_a_lacuna()
    {
        // Capa e anúncio sem número; "ilegível" é interpolado; depois da última página confirmada
        // nada se inventa — o ano no cabeçalho não vira número de página.
        IReadOnlyList<string>[] margens = [[], [], ["-1-"], ["-2-"], ["ilegível"], ["-4-"], ["JANEIRO DE 1960"], []];
        var r = PaginacaoImpressa.Calcular(margens);
        Assert.Equal([null, null, "1", "2", "3", "4", null, null], r.Select(x => x.Impressa));
        Assert.Equal(OrigemImpressa.Interpolada, r[4].Origem);
    }

    [Fact]
    public void Pagina_dupla_recebe_as_duas_impressas()
    {
        IReadOnlyList<string>[] margens =
        [
            ["34 Rev. Exemplo, Cidade, t. 56, jan./fev. 2015"], ["36 Rev. Exemplo, Cidade, t. 56, jan./fev. 2015"],
            ["38 Rev. Exemplo, Cidade, t. 56, jan./fev. 2015", "Rev. Exemplo, 2015 39"],
        ];
        Assert.Equal(["34-35", "36-37", "38-39"], PaginacaoImpressa.Calcular(margens).Select(x => x.Impressa));
    }

    [Fact]
    public void Ajuste_da_curadoria_vence_a_deteccao() =>
        Assert.Equal("ii", PaginacaoImpressa.Calcular([["-1-"], ["-2-"]], new Dictionary<int, string> { [2] = "ii" })[1].Impressa);
}

public class BuscaTestes
{
    [Theory]
    [InlineData("colonos", "colono")]
    [InlineData("colonias", "colonia")]
    [InlineData("padres", "padr")]
    [InlineData("padre", "padr")]
    [InlineData("lugares", "lugar")]
    [InlineData("professora", "professor")]
    [InlineData("igrejas", "igreja")]
    [InlineData("imigracoes", "imigracao")]
    public void Radical_junta_plural_e_genero(string palavra, string radical) =>
        Assert.Equal(radical, RadicalPortugues.Radical(palavra));

    [Fact]
    public void Distancia_de_ocr_aceita_as_trocas_medidas_e_recusa_palavra_distante()
    {
        Assert.True(DistanciaOcr.Calcular("edificacao", "edificaqao") <= 0.5);
        Assert.True(DistanciaOcr.Calcular("comunidade", "cornunidade") <= 0.5);
        Assert.True(DistanciaOcr.Calcular("colonizacao", "eoionizacao") <= DistanciaOcr.Tolerancia(11));
        Assert.True(DistanciaOcr.Calcular("ponte", "fonte") > DistanciaOcr.Tolerancia(5) || DistanciaOcr.Calcular("ponte", "fonte") >= 1);
        Assert.True(DistanciaOcr.Calcular("casa", "cama") > DistanciaOcr.Tolerancia(4));
    }

    [Fact]
    public void Consulta_entende_frase_prefixo_e_exclusao()
    {
        var t = Consulta.Interpretar("\"Ponte de Ferro\" coloni* -enchente Müller");
        Assert.Equal(4, t.Count);
        Assert.True(t[0].Frase);
        Assert.Equal(["ponte", "de", "ferro"], t[0].Palavras);
        Assert.True(t[1].Prefixo);
        Assert.True(t[2].Negado);
        Assert.Equal(["muller"], t[3].Palavras);
        Assert.Equal("\"ponte de ferro\" AND \"coloni\"* AND \"muller\" NOT \"enchente\"", Consulta.ParaFts(t, Consulta.Exata));
    }

    [Fact]
    public void Pontuacao_do_usuario_nunca_vira_operador()
    {
        var fts = Consulta.ParaFts(Consulta.Interpretar("NOT OR AND ( ) 15-11"), Consulta.Exata);
        Assert.Equal("\"not\" AND \"or\" AND \"and\" AND \"15 11\"", fts);
    }

    [Fact]
    public void Trecho_destaca_a_palavra_original_com_acento() =>
        Assert.Contains(GeradorDeTrechos.Gerar("A colônia cresceu.", p => p == "colonia")[0].Segmentos,
            s => s.Destaque && s.Texto == "colônia");
}

public class ColecaoTestes
{
    private static readonly Colecao RevistaExemplo = Colecoes.Ler(Caminhos.Teste("revista-exemplo.json")).Single();

    [Theory]
    [InlineData("Tomo XXIX - n.01 Janeiro.pdf", "XXIX", "01", "Janeiro")]
    [InlineData("omo XIII -n.02 Fevereiro.pdf", "XIII", "02", "Fevereiro")]
    [InlineData("Tomo V - n.06 a 10 Junho-Outubro.pdf", "V", "06 a 10", "Junho-Outubro")]
    [InlineData("Tomo VIII n. 12 Outubro.pdf", "VIII", "12", "Outubro")]
    [InlineData("Tomo 59 - n.01 - Janeiro-fevereiro.pdf", "59", "01", "Janeiro-fevereiro")]
    [InlineData("Revista Exemplo 16 - 1975 - 2.pdf", "16", "2", null)]
    public void Nome_do_arquivo_sugere_os_campos(string arquivo, string tomo, string numero, string? meses)
    {
        var c = RevistaExemplo.SugerirCampos(arquivo);
        Assert.Equal(tomo, c["tomo"]);
        Assert.Equal(numero, c["numero"]);
        Assert.Equal(meses, c.GetValueOrDefault("meses"));
    }

    [Fact]
    public void Titulo_sem_mes_nao_deixa_pontuacao_orfa() =>
        Assert.Equal("Revista Exemplo — Tomo VII, n.º 09 (1966)",
            RevistaExemplo.TituloDoDocumento(new Dictionary<string, string> { ["ano"] = "1966", ["tomo"] = "VII", ["numero"] = "09" }, "Tomo VII - n.09.pdf"));

    [Fact]
    public void Destino_sem_ano_nunca_sai_da_pasta_do_acervo()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "acervo-teste");
        var d = new RepositorioDoAcervo(raiz).Destino(RevistaExemplo, new Dictionary<string, string> { ["tomo"] = "I" }, "Tomo I - n.03 Janeiro.pdf");
        Assert.StartsWith(Path.Combine(raiz, "revista-exemplo"), Path.GetFullPath(d.Pdf));
    }

    [Fact]
    public void Pasta_vem_da_decada_e_do_ano() =>
        Assert.Equal("1980-1989/1988", RevistaExemplo.PastaDoDocumento(new Dictionary<string, string> { ["ano"] = "1988" }));
}

/// <summary>
/// Uma coleção que não é uma revista por tomos — um jornal datado, guardado por ano e mês — para
/// garantir que o núcleo é genérico de verdade: pasta, título, validação, ano e desfalques.
/// </summary>
public class ColecaoGenericaTestes
{
    private static readonly Colecao Jornal = new()
    {
        Id = "jornal-teste",
        Nome = "Jornal de Teste",
        Pastas = "{ano}/{mes}",
        Titulo = "Jornal de Teste — {data} — {caderno}",
        Campos =
        [
            new CampoDaColecao("data", "Data", true) { Tipo = TiposDeCampo.Data },
            new CampoDaColecao("edicao", "Edição") { Tipo = TiposDeCampo.Numero },
            new CampoDaColecao("caderno", "Caderno") { Tipo = TiposDeCampo.Opcoes, Opcoes = ["Geral", "Esportes"] },
        ],
    };

    private static readonly Colecao Revista = Colecoes.Ler(Caminhos.Teste("revista-exemplo.json")).Single();

    private static Dictionary<string, string> C(params (string, string)[] pares) => pares.ToDictionary(p => p.Item1, p => p.Item2);

    [Fact]
    public void Jornal_guarda_por_ano_e_mes_tirados_da_data()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "acervo-teste");
        var d = new RepositorioDoAcervo(raiz).Destino(Jornal, C(("data", "15/03/1988")), "pagina-1.pdf");
        Assert.Equal(Path.Combine(raiz, "jornal-teste", "1988", "03", "pagina-1.pdf"), Path.GetFullPath(d.Pdf));
        Assert.Equal(1988, Jornal.Ano(C(("data", "15/03/1988"))));
        Assert.Equal(1988, Revista.Ano(C(("ano", "1988"))));
    }

    [Fact]
    public void Validacao_segue_o_tipo_de_cada_campo()
    {
        Assert.Empty(Jornal.ValidarCampos(C(("data", "15/03/1988"), ("edicao", "1234"), ("caderno", "geral"))));
        Assert.Contains(Jornal.ValidarCampos(C()), e => e.StartsWith("Data: obrigatório"));
        Assert.Contains(Jornal.ValidarCampos(C(("data", "março de 88"))), e => e.StartsWith("Data:"));
        Assert.Contains(Jornal.ValidarCampos(C(("data", "1988"))), e => e.Contains("por mês"));
        Assert.Contains(Jornal.ValidarCampos(C(("data", "15/03/1988"), ("edicao", "doze"))), e => e.StartsWith("Edição:"));
        Assert.Contains(Jornal.ValidarCampos(C(("data", "15/03/1988"), ("caderno", "Política"))), e => e.StartsWith("Caderno:"));
        Assert.Contains(Revista.ValidarCampos(C(("tomo", "I"), ("ano", "88"))), e => e.StartsWith("Ano:"));
    }

    [Fact]
    public void Titulo_sem_um_campo_nao_deixa_traco_sobrando()
    {
        Assert.Equal("Jornal de Teste — 15/03/1988 — Geral", Jornal.TituloDoDocumento(C(("data", "15/03/1988"), ("caderno", "Geral")), "x.pdf"));
        Assert.Equal("Jornal de Teste — 15/03/1988", Jornal.TituloDoDocumento(C(("data", "15/03/1988")), "x.pdf"));
    }

    [Fact]
    public void Campos_que_definem_a_pasta_ficam_travados()
    {
        Assert.Equal(["data"], Jornal.CamposDaPasta());
        Assert.Equal(["ano"], Revista.CamposDaPasta());
    }

    [Fact]
    public void Colecao_sem_numeracao_nao_tem_desfalques() =>
        Assert.Empty(App.Desfalques.Calcular(Jornal, [C(("data", "15/03/1988"))], 2026));

    [Fact]
    public void Desfalques_seguem_a_numeracao_configurada()
    {
        // Tomo VIII completo, mas escrito ora "VIII" ora "8"; tomo IX sem o n.º 5; nada em 1964.
        IReadOnlyList<IReadOnlyDictionary<string, string>> fichas =
        [
            .. Enumerable.Range(1, 6).Select(n => C(("ano", "1963"), ("tomo", "VIII"), ("numero", n.ToString("00")))),
            C(("ano", "1963"), ("tomo", "8"), ("numero", "07 a 12")),
            .. new[] { "01-04", "06", "07", "08", "09", "10", "11-12" }.Select(n => C(("ano", "1965"), ("tomo", "IX"), ("numero", n))),
        ];
        var lista = App.Desfalques.Calcular(Revista, fichas, 1966);
        Assert.Equal(new HashSet<string> { "IX/05", "—/1964", "X/1966" },
            lista.Select(d => d.Numero == "todas" ? $"{d.Grupo}/{d.Ano}" : $"{d.Grupo}/{d.Numero}").ToHashSet());
    }

    [Fact]
    public void Numeros_romanos_e_faixas()
    {
        Assert.Equal(29, Numeros.Valor("XXIX"));
        Assert.Equal(29, Numeros.Valor("29"));
        Assert.Equal([6, 7, 8, 9, 10], Numeros.Cobertos("06 a 10").Order());
        Assert.Equal([11, 12], Numeros.Cobertos("011-12").Order());
        Assert.Equal("LXVI", Numeros.ComoExemplo(66, "LII"));
        Assert.Equal("66", Numeros.ComoExemplo(66, "65"));
    }

    [Fact]
    public void Colecoes_atuais_e_modelos_passam_na_validacao_do_cadastro()
    {
        Assert.Empty(Colecoes.Validar(Revista, []));
        Assert.Empty(Colecoes.Validar(Jornal, [Revista]));
        foreach (var m in BibliotecaDigital.App.ModelosDeColecao.Todos)
        {
            var c = m.Criar() with { Id = "nova-" + m.Id, Nome = "Nova " + m.Nome };
            Assert.Empty(Colecoes.Validar(c, [Revista]));
        }
    }

    [Fact]
    public void Cadastro_recusa_o_que_quebraria_o_acervo()
    {
        Assert.Contains(Colecoes.Validar(Jornal with { Id = "Jornal NSC" }, []), e => e.StartsWith("Identificador"));
        Assert.Contains(Colecoes.Validar(Jornal with { Id = Revista.Id }, [Revista]), e => e.StartsWith("Já existe"));
        Assert.Contains(Colecoes.Validar(Jornal with { Pastas = "{secao}" }, []), e => e.Contains("{secao}"));
        Assert.Contains(Colecoes.Validar(Jornal with { Titulo = "{pagina}" }, []), e => e.Contains("{pagina}"));
        Assert.Contains(Colecoes.Validar(Jornal with { NomesDeArquivo = ["(?<data"] }, []), e => e.StartsWith("Expressão inválida"));
        Assert.Contains(Colecoes.Validar(Jornal with { Campos = [.. Jornal.Campos, new CampoDaColecao("mes", "Mês")] }, []), e => e.Contains("reservado"));
        Assert.Contains(Colecoes.Validar(Jornal with { Campos = [.. Jornal.Campos, new CampoDaColecao("data", "Outra data")] }, []), e => e.StartsWith("Há dois campos"));
        Assert.Contains(Colecoes.Validar(Jornal with { Campos = [new CampoDaColecao("x", "X") { Tipo = TiposDeCampo.Opcoes }], Pastas = "", Titulo = "{x}" }, []),
            e => e.Contains("liste as opções"));
        // Sem campo de data não há de onde tirar o ano nem o mês da pasta.
        Assert.Contains(Colecoes.Validar(Jornal with { Campos = [new CampoDaColecao("titulo", "Título")], Titulo = "{titulo}" }, []), e => e.Contains("{ano}"));
        Assert.Contains(Colecoes.Validar(Jornal with { Numeracao = new NumeracaoDaColecao { Grupo = "volume", Numero = "edicao" } }, []), e => e.Contains("“volume”"));
    }

    [Fact]
    public void Colecao_gravada_e_lida_de_volta_e_a_mesma()
    {
        var arquivo = Path.Combine(Path.GetTempPath(), $"colecoes-{Guid.NewGuid():N}.json");
        try
        {
            Colecoes.Gravar(arquivo, [Revista, Jornal]);
            var lidas = Colecoes.Ler(arquivo);
            Assert.Equal(2, lidas.Count);
            Assert.Equal(Revista.Pastas, lidas[0].Pastas);
            Assert.Equal(Revista.Titulo, lidas[0].Titulo);
            Assert.Equal(Revista.NomesDeArquivo, lidas[0].NomesDeArquivo);
            Assert.Equal(Revista.Extracao.Margens, lidas[0].Extracao.Margens);
            Assert.Equal(Revista.Numeracao!.Faixas, lidas[0].Numeracao!.Faixas);
            Assert.Equal(Jornal.Campos.Select(c => (c.Chave, c.Tipo)), lidas[1].Campos.Select(c => (c.Chave, c.Tipo)));
            Assert.Equal(["Geral", "Esportes"], lidas[1].Campos[2].Opcoes!);
        }
        finally { File.Delete(arquivo); }
    }
}
