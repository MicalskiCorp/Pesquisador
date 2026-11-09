// Biblioteca Digital — o pouco de JavaScript que a tela usa. Nenhuma biblioteca externa; sem ele a tela
// funciona (só sem a rolagem vinculada e os atalhos).
(function () {
  'use strict'

  function guardar(chave, valor) { try { localStorage.setItem('biblioteca.' + chave, valor) } catch (e) { } }
  function ler(chave) { try { return localStorage.getItem('biblioteca.' + chave) } catch (e) { return null } }

  // Modo de busca, coleção e variantes: troca e já busca de novo.
  document.querySelectorAll('[data-enviar-ao-mudar]').forEach(function (el) {
    el.addEventListener('change', function () { el.form.requestSubmit ? el.form.requestSubmit() : el.form.submit() })
  })

  // ---------------------------------------------------------------- documento: PDF e texto juntos
  var colPdf = document.getElementById('paginas-pdf')
  var colTexto = document.querySelector('.lado-a-lado .texto')
  if (colPdf && colTexto) documento(colPdf, colTexto)

  function documento(colPdf, colTexto) {
    var vincular = document.getElementById('vincular')
    var campoPagina = document.getElementById('pagina-pdf')
    var impressa = document.getElementById('impressa-atual')
    var original = document.getElementById('abrir-original')
    var silencio = new Map() // coluna -> até quando ignorar a rolagem (foi o código que rolou)
    var atual = 0

    if (ler('vincular') === 'nao') vincular.checked = false
    vincular.addEventListener('change', function () { guardar('vincular', vincular.checked ? 'sim' : 'nao') })

    function paginaPdf(n) { return document.getElementById('pdf-' + n) }
    function paginaTexto(n) { return document.getElementById('pagina-' + n) }

    // A página no topo da coluna e quanto dela já passou (0 a 1).
    function posicao(col) {
      var seletor = col === colPdf ? '.pagina-pdf' : '.folha'
      var y = col.scrollTop + 12
      var els = col.querySelectorAll(seletor)
      var el = els[0]
      for (var i = 0; i < els.length; i++) { if (els[i].offsetTop <= y) el = els[i]; else break }
      if (!el) return null
      var fracao = el.offsetHeight ? (y - el.offsetTop) / el.offsetHeight : 0
      return { n: parseInt(el.getAttribute('data-numero'), 10), fracao: Math.max(0, Math.min(1, fracao)) }
    }

    function levar(col, el, fracao) {
      if (!el) return
      silencio.set(col, performance.now() + 150)
      col.scrollTop = el.offsetTop + fracao * el.offsetHeight - 12
    }

    function marcar(n) {
      if (n === atual) return
      atual = n
      document.querySelectorAll('.folha.atual, .pagina-pdf.atual').forEach(function (e) { e.classList.remove('atual') })
      var t = paginaTexto(n), p = paginaPdf(n)
      if (t) t.classList.add('atual')
      if (p) p.classList.add('atual')
      campoPagina.value = n
      var imp = t ? t.getAttribute('data-impressa') : ''
      impressa.style.display = imp ? '' : 'none'
      impressa.textContent = 'impressa ' + imp
      original.href = original.getAttribute('data-base') + '#page=' + n
      var url = new URL(location.href)
      url.searchParams.set('p', n)
      history.replaceState(null, '', url)
    }

    // Rolou uma coluna: a outra vai para a mesma página, na mesma proporção.
    function aoRolar(col) {
      var agendado = false
      col.addEventListener('scroll', function () {
        if (agendado) return
        agendado = true
        requestAnimationFrame(function () {
          agendado = false
          if (performance.now() < (silencio.get(col) || 0)) return
          var pos = posicao(col)
          if (!pos) return
          marcar(pos.n)
          if (!vincular.checked) return
          var outra = col === colPdf ? colTexto : colPdf
          levar(outra, outra === colPdf ? paginaPdf(pos.n) : paginaTexto(pos.n), pos.fracao)
        })
      }, { passive: true })
    }
    aoRolar(colPdf)
    aoRolar(colTexto)

    // Ir a uma página: as duas colunas, do topo dela.
    function irPara(n) {
      levar(colPdf, paginaPdf(n), 0)
      levar(colTexto, paginaTexto(n), 0)
      marcar(n)
    }
    document.querySelectorAll('[data-pagina]').forEach(function (a) {
      a.addEventListener('click', function (e) { e.preventDefault(); irPara(parseInt(a.getAttribute('data-pagina'), 10)) })
    })
    document.getElementById('form-pagina').addEventListener('submit', function (e) {
      var n = parseInt(campoPagina.value, 10)
      if (n > 0 && paginaPdf(n)) { e.preventDefault(); irPara(n) }
    })

    // Clicar num parágrafo: ele fica marcado e o PDF vai ao ponto da página onde ele está.
    colTexto.addEventListener('click', function (e) {
      var par = e.target.closest('.folha p')
      var sel = window.getSelection()
      if (!par || (sel && sel.toString().trim())) return
      var folha = par.closest('.folha')
      document.querySelectorAll('.folha p.selecionado').forEach(function (x) { x.classList.remove('selecionado') })
      par.classList.add('selecionado')
      var n = parseInt(folha.getAttribute('data-numero'), 10)
      var fracao = folha.offsetHeight ? (par.offsetTop - folha.offsetTop) / folha.offsetHeight : 0
      levar(colPdf, paginaPdf(n), Math.max(0, fracao - 0.05))
      marcar(n)
    })

    // Zoom do PDF, mantendo a página e o ponto em que se estava.
    var zooms = [1, 1.5, 2, 3]
    var zoom = Math.max(0, zooms.indexOf(parseFloat(ler('zoom') || '1')))
    function aplicarZoom() {
      var pos = posicao(colPdf)
      colPdf.style.setProperty('--zoom', zooms[zoom])
      colPdf.querySelectorAll('img').forEach(function (img) { img.sizes = Math.round(50 * zooms[zoom]) + 'vw' })
      if (pos) levar(colPdf, paginaPdf(pos.n), pos.fracao)
      guardar('zoom', zooms[zoom])
    }
    document.querySelectorAll('[data-zoom]').forEach(function (b) {
      b.addEventListener('click', function () {
        zoom = Math.max(0, Math.min(zooms.length - 1, zoom + parseInt(b.getAttribute('data-zoom'), 10)))
        aplicarZoom()
      })
    })
    if (zoom > 0) aplicarZoom()

    // O termo buscado, marcado sobre a imagem — só nas páginas em que ele aparece.
    var realces = colPdf.getAttribute('data-realces')
    if (realces) {
      colPdf.querySelectorAll('.pagina-pdf[data-ocorre]').forEach(function (fig) {
        var img = fig.querySelector('img')
        var feito = false
        function desenhar() {
          if (feito) return
          feito = true
          fetch(realces + '&p=' + fig.getAttribute('data-numero')).then(function (r) { return r.json() }).then(function (caixas) {
            caixas.forEach(function (c) {
              var d = document.createElement('span')
              d.className = 'realce'
              d.style.left = (c.x * 100) + '%'
              d.style.top = (c.y * 100) + '%'
              d.style.width = (c.l * 100) + '%'
              d.style.height = (c.a * 100) + '%'
              fig.appendChild(d)
            })
          }).catch(function () { })
        }
        if (img.complete && img.naturalWidth) desenhar(); else img.addEventListener('load', desenhar)
      })
    }

    // Abre na página pedida.
    var inicial = parseInt(campoPagina.value, 10) || 1
    irPara(inicial)
    window.addEventListener('load', function () { irPara(inicial) })
  }

  // ---------------------------------------------------------------- errata pela seleção
  var botaoErrata = document.getElementById('errata-flutuante')
  if (botaoErrata && colTexto) {
    colTexto.addEventListener('mouseup', function () {
      var sel = window.getSelection()
      var literal = sel ? sel.toString().trim() : ''
      var no = sel && sel.anchorNode ? (sel.anchorNode.nodeType === 1 ? sel.anchorNode : sel.anchorNode.parentElement) : null
      var folha = no ? no.closest('.folha') : null
      if (!literal || literal.length > 200 || !folha) { botaoErrata.style.display = 'none'; return }
      var r = sel.getRangeAt(0).getBoundingClientRect()
      botaoErrata.href = botaoErrata.getAttribute('data-base') + '&pagina=' + folha.getAttribute('data-numero') + '&literal=' + encodeURIComponent(literal)
      botaoErrata.style.left = (r.left + window.scrollX) + 'px'
      botaoErrata.style.top = (r.bottom + window.scrollY + 6) + 'px'
      botaoErrata.style.display = 'inline-flex'
    })
  }

  // ---------------------------------------------------------------- inclusão: destino ao vivo
  var formFicha = document.getElementById('form-inclusao')
  if (formFicha) {
    var espera
    formFicha.addEventListener('input', function () {
      clearTimeout(espera)
      espera = setTimeout(function () {
        var campos = {}
        formFicha.querySelectorAll('[data-campo]').forEach(function (i) { campos[i.getAttribute('data-campo')] = i.value })
        fetch('/curadoria/destino', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ colecao: formFicha.getAttribute('data-colecao'), campos: campos, nomeDoArquivo: formFicha.querySelector('[name=NomeDoArquivo]').value })
        }).then(function (r) { return r.json() }).then(function (d) {
          document.getElementById('destino-caminho').textContent = d.caminho
          document.getElementById('destino-titulo').textContent = d.titulo
          var aviso = document.getElementById('destino-aviso')
          aviso.textContent = d.erro || (d.existe ? 'Já existe um arquivo neste lugar. Se for o mesmo PDF, o texto é refeito e a ficha é mantida; se for outro, a inclusão é recusada.' : '')
          aviso.style.display = aviso.textContent ? 'block' : 'none'
        }).catch(function () { })
      }, 250)
    })
  }
})()
