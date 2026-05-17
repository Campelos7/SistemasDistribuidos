namespace PreProcessamento.Services;

/// <summary>
/// Converte valores entre escalas (ex.: Fahrenheit para Celsius).
/// Usado no pré-processamento antes da agregação no gateway.
/// </summary>
public class EscalaConverter
{
    /// <summary>
    /// Converte temperatura de Fahrenheit para Celsius quando o valor parece estar em °F (> 50).
    /// </summary>
    public double NormalizarTemperatura(double valor, string tipoDado)
    {
        if (!tipoDado.Equals("TEMP", StringComparison.OrdinalIgnoreCase) &&
            !tipoDado.Equals("TEMPERATURA", StringComparison.OrdinalIgnoreCase))
            return valor;

        // Valores acima de 50 em contexto urbano assumem escala Fahrenheit
        if (valor > 50)
            return (valor - 32) * 5.0 / 9.0;

        return valor;
    }

    /// <summary>
    /// Converte humidade de escala 0-1 para percentagem 0-100 se necessário.
    /// </summary>
    public double NormalizarHumidade(double valor, string tipoDado)
    {
        if (!tipoDado.Equals("HUM", StringComparison.OrdinalIgnoreCase) &&
            !tipoDado.Equals("HUMIDADE", StringComparison.OrdinalIgnoreCase))
            return valor;

        if (valor > 0 && valor <= 1)
            return valor * 100;

        return valor;
    }
}
