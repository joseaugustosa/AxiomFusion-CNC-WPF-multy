namespace AxiomFusion.CncController.Core;

public static class GrblConstants
{
    public static readonly IReadOnlyDictionary<int, string> Errors = new Dictionary<int, string>
    {
        {  1, "G-code letter sem número" },
        {  2, "Valor negativo em campo não-negativo" },
        {  3, "Valor S negativo" },
        {  4, "Valor de avanço inválido" },
        {  5, "Reset durante ciclo" },
        {  6, "Mensagem do sistema: interrupção Soft-Limit" },
        {  7, "Endereço de linha inválido" },
        {  8, "Distância de arco negativa" },
        {  9, "G2/G3 sem plano de arco" },
        { 10, "G2/G3 sem parâmetros de arco" },
        { 11, "G2/G3 distância errada" },
        { 12, "Múltiplos G: dois G de uma mesma categoria" },
        { 13, "Valor de offset errado" },
        { 14, "Número de dígitos de endereço errado" },
        { 15, "Valor de F negativo" },
        { 16, "N inesperado" },
        { 17, "Multiplicadores de offset inválidos" },
        { 18, "P inválido em G10" },
        { 19, "Eixo ausente em G10" },
        { 20, "G0 proibido em modo Laser" },
        { 21, "Sem suporte a eixo U" },
        { 22, "Distância de arco demasiado pequena" },
        { 23, "Conflito de comandos de eixo" },
        { 24, "G-code de movimento sem eixos" },
        { 25, "Plano de arco fora do plano de movimento" },
        { 26, "G2/G3 num plano não-XY inválido" },
        { 27, "Raio de arco inválido" },
        { 28, "G10 com campo ausente" },
        { 29, "Valor de dwell inválido" },
        { 30, "Número de linha inválido" },
        { 33, "Feed rate não definido" },
        { 34, "S inválido em modo Laser" },
    };

    public static readonly IReadOnlyDictionary<int, string> Alarms = new Dictionary<int, string>
    {
        { 1,  "Hard limit activado — parar e bloquear" },
        { 2,  "Soft limit activado — parar e bloquear" },
        { 3,  "Reset durante ciclo — posição perdida" },
        { 4,  "Sonda falhou — contacto não esperado" },
        { 5,  "Sonda não encontrou o alvo" },
        { 6,  "Reset após falha de homing" },
        { 7,  "Distância máxima de jog ultrapassada" },
        { 8,  "Falha no ciclo de homing — ciclo não completo" },
        { 9,  "Homing falhou — velocidade zero" },
        { 10, "Homing falhou — interruptor não desbloqueado" },
        { 11, "Homing falhou — interruptor não encontrado" },
    };

    public static readonly IReadOnlyDictionary<string, (string Label, string Color)> States =
        new Dictionary<string, (string, string)>
    {
        { "Idle",  ("Inativo",    "#2ecc71") },
        { "Run",   ("A cortar",   "#3498db") },
        { "Hold",  ("Parado",     "#f39c12") },
        { "Jog",   ("Jog",        "#9b59b6") },
        { "Alarm", ("ALARME",     "#e74c3c") },
        { "Door",  ("Porta",      "#e74c3c") },
        { "Check", ("Verificar",  "#f39c12") },
        { "Home",  ("Homing",     "#3498db") },
        { "Sleep", ("Hibernando", "#6c7086") },
    };

    public static string GetError(int code)  => Errors.TryGetValue(code,  out var v) ? v : $"Erro {code} desconhecido";
    public static string GetAlarm(int code)  => Alarms.TryGetValue(code,  out var v) ? v : $"Alarme {code} desconhecido";
    public static (string Label, string Color) GetState(string s)
        => States.TryGetValue(s, out var v) ? v : (s, "#6c7086");
}
