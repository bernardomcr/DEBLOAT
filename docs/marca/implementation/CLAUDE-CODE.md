# Implementar a identidade DEBLOAT!

Trate este arquivo, tokens.json, os SVGs e o PDF como a especificação visual. Adapte os componentes ao framework já usado no aplicativo; não troque a arquitetura nem implemente funções destrutivas como parte do trabalho visual.

## Marca
- Nome escrito DEBLOAT! em materiais de marca; ID técnico de arquivo pode usar Debloat.
- Usar vetores fornecidos. Não recriar o wordmark com texto, não substituir o símbolo por emoji ou ícone de biblioteca.
- Marca na tela Sobre: assinatura horizontal com largura 280–420 px. Ícone na barra de título: 20 px. Nome ao lado em Segoe UI 12 px. Não colocar a logo grande em cada aba.
- Assinatura light sobre fundo claro; dark sobre fundo escuro. Mono black/white para usos de uma tinta. Não esticar, girar ou adicionar efeitos.
- Respiro mínimo ao redor da assinatura: 25% da altura do símbolo. Wordmark isolado: 25% da altura das letras. Mínimos: assinatura 200 px, wordmark 140 px, tile 24 px (16/20 reservados ao sistema), bandeja simplificada 16 px.

## Janela e hierarquia
- Janela padrão 1040×720 DIP, mínimo 880×640; suportar DPI 100–200% e rolagem no conteúdo quando necessário.
- Preservar moldura e controles de janela nativos. Fundo branco / cinza #202124, conforme tema do sistema ou escolha persistida.
- Quatro abas fixas: Windows, Personalizar, Presets, Gerar. Área de aba 44 px, indicador ativo inferior 3 px, sem saltos de layout.
- Conteúdo com margem 32 px. Título 28/36 semibold, subtítulo 14/21, grupo 18/26 semibold. Não aplicar letras inclinadas da marca aos textos de UI.
- Listas com divisórias discretas; evitar um card por controle. Linhas com altura mínima 64 px, rótulo e explicação à esquerda, estado/controle à direita.
- Rodapé de ações estável; uma ação principal por etapa. Laranja de marca #F4512A é decorativo. Para texto/botão pequeno usar --accent de cada tema, com contraste validado.

## Estados e acessibilidade
- Toggle à direita significa exatamente o verbo do rótulo. Ex.: “Remover apps sugeridos”: ligado = remover. Ex.: “Manter Microsoft Store”: ligado = manter. Não inverter isso internamente.
- Grupo avançado deve informar efeitos concretos e dependências. Não chamar remoção de proteção ou atualizações de “inútil”. Presets reais precisam de validação técnica separada.
- Foco visível 2 px, offset 3 px. Tab percorre controles; setas navegam abas. Controles nativos sempre que possível.
- Hover 120 ms; troca de painel 160 ms opcional, sem piscar. Respeitar reduzir movimento.
- Erro = texto claro + indicação visual, nunca só vermelho. Laranja da marca não substitui o estado de alerta.
- Download em andamento deve mostrar bytes, progresso e velocidade reais. Não simular porcentagem. Desabilitar apenas ações incompatíveis e oferecer cancelamento se a operação permitir.
- Resumo antes de gerar: fonte/versão/edição/idioma reais, preset e alterações, caminho de saída. Ações de gravar em mídia devem mostrar qual dispositivo será usado e requerer confirmação específica.

## Microcopy
Curta, direta, amigável. Botões: “Escolher ISO”, “Revisar alterações”, “Gerar arquivo”. Confirmação: “Arquivo pronto.” Falha: “Não foi possível baixar a ISO. Verifique a conexão e tente novamente.” Reservar “DEBLOAT!” para a marca; evitar gritos e piadas em avisos.

## Referência visual
O HTML é um protótipo de identidade. Seus botões não baixam nem modificam Windows. Os exemplos não determinam quais serviços devem ser removidos. Preserve os comportamentos reais e o preset técnico do projeto existente.

## Critérios de conclusão
Confira temas claro/escuro, fonte Segoe UI no Windows, DPI 100/150/200%, foco de teclado, ícone na barra de tarefas, legibilidade em 16/24/32 px, textos longos sem corte e estado de download sem mudança de layout. Compare o app com as mockups do kit.
