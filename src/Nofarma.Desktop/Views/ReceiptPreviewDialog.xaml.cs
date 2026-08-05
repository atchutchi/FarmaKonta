using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nofarma.Application.Sales;
using Nofarma.Domain.Sales;

namespace Nofarma.Desktop.Views;

public sealed partial class ReceiptPreviewDialog : ContentDialog
{
    public ReceiptPreviewDialog(ReceiptDetails receipt)
    {
        InitializeComponent();
        PharmacyText.Text = receipt.PharmacyName;
        DocumentLabelText.Text = receipt.DocumentLabel;
        NumberText.Text = $"N.º {receipt.Number}";
        DateText.Text = $"Data UTC: {receipt.CreatedAtUtc.Value:dd/MM/yyyy HH:mm}";
        OperatorText.Text = $"Operador: {receipt.OperatorName}";
        TotalText.Text = FormatXof(receipt.TotalXof);
        ChangeText.Text = FormatXof(receipt.ChangeXof);

        foreach (ReceiptLineDetails line in receipt.Lines)
        {
            ReceiptLinesPanel.Children.Add(CreateLine(line));
        }
        foreach (ReceiptPaymentDetails payment in receipt.Payments)
        {
            ReceiptPaymentsPanel.Children.Add(CreatePayment(payment));
        }
    }

    private void OnSimulatePrint(object sender, RoutedEventArgs e)
    {
        SimulationStatus.Message = "Enviado ao simulador";
        SimulationStatus.IsOpen = true;
    }

    private static Grid CreateLine(ReceiptLineDetails line)
    {
        var grid = new Grid { RowSpacing = 2 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var description = new TextBlock
        {
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = line.Description,
            TextWrapping = TextWrapping.Wrap
        };
        var detail = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["NofarmaMutedBrush"],
            Text = $"{line.QuantityPackages} × {line.UnitName} a {FormatXof(line.UnitPriceXof)}"
        };
        var amount = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = FormatXof(line.TotalXof)
        };
        Grid.SetRow(detail, 1);
        Grid.SetRow(amount, 1);
        grid.Children.Add(description);
        grid.Children.Add(detail);
        grid.Children.Add(amount);
        return grid;
    }

    private static Grid CreatePayment(ReceiptPaymentDetails payment)
    {
        var grid = new Grid();
        grid.Children.Add(new TextBlock { Text = PaymentName(payment.Method) });
        grid.Children.Add(new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Text = FormatXof(payment.AmountXof)
        });
        return grid;
    }

    private static string PaymentName(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Dinheiro",
        PaymentMethod.Card => "Cartão",
        PaymentMethod.MobileMoney => "Dinheiro móvel",
        PaymentMethod.BankTransfer => "Transferência",
        _ => "Pagamento"
    };

    private static string FormatXof(long amount) => $"{amount:N0} XOF";
}
