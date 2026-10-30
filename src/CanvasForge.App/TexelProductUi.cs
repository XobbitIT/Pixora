using System.Windows;
using System.Windows.Controls;
using CanvasForge.Core;
namespace CanvasForge.App;
internal sealed partial class MainWindow
{
    private void BuildTexelProductUi(StackPanel page)
    {
        var area=new StackPanel();page.Children.Add(new Expander{Header=T("Тексельний маршрут Standard — дослідний","Standard texel route — research"),Content=area});
        area.Children.Add(Text(T("Калібрування 26.10, кінці штрихів 29.10: лише той самий вид полотна, Size 3, чорний і прозорий фон. 1 піксель файлу = 1 тексель; до 40×40, без масштабування. Фізична якість ще не сертифікована.",
            "Calibration 26 Oct, stroke ends 29 Oct: same Canvas view, Size 3, black and transparency only. 1 source pixel = 1 texel; up to 40×40, no resizing. Physical quality is not certified."),12,Warning));
        AddCheck(area,TexelExperimentSettings.EnabledKey,T("Увімкнути тексельний маршрут для кнопки Почати","Use texel route for Start"),true);
        AddNumber(area,TexelExperimentSettings.ColumnKey,T("Початковий стовпець (75–459)","First column (75–459)"),true);
        AddNumber(area,TexelExperimentSettings.RowKey,T("Початковий рядок (198–537)","First row (198–537)"),true);
        area.Children.Add(Text(T("Кінець кожного штриха — правило повного покриття, перевірене картинками 8 і 9. Рядки без надійної адресації відхиляються; наживо перевірені рядки 203–242, 452–475 і 493–532. Рух зафіксований як у пілоті; інші режими відхиляються з причиною.",
            "Each stroke ends by the full-coverage rule verified by Pictures 8 and 9. Rows without reliable addressing are refused; rows 203–242, 452–475 and 493–532 were verified live. Motion is fixed to the pilot; unsupported modes fail with a reason."),12,Muted));
    }
}
