using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using FinancialAnalysis.Configuration;

namespace FinancialAnalysis.Tests
{
    public class ExcelTests
    {
        private readonly App _app;

        public ExcelTests()
        {
            var logger = new Mock<ILogger<App>>();
            var settings = new AppSettings(); // Create with default values
            var httpClientFactory = new Mock<IHttpClientFactory>();
            
            _app = new App(
                logger.Object,
                settings,
                httpClientFactory.Object
            );
        }

        [Theory]
        [InlineData(1, "A")]
        [InlineData(2, "B")]
        [InlineData(26, "Z")]
        [InlineData(27, "AA")]
        [InlineData(28, "AB")]
        [InlineData(35, "AB")]
        [InlineData(37, "AB")]
        [InlineData(52, "AZ")]
        [InlineData(53, "BA")]
        [InlineData(702, "ZZ")]
        [InlineData(703, "AAA")]
        [InlineData(16384, "XFD")] // Excel's maximum column
        public void GetColumnLetter_ShouldReturnCorrectExcelColumn(int columnIndex, string expected)
        {
            // Act
            var result = _app.GetColumnLetter(columnIndex);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void GetColumnLetter_WithInvalidInput_ShouldThrowArgumentException(int columnIndex)
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => _app.GetColumnLetter(columnIndex));
            Assert.Contains("Column index must be positive", exception.Message);
        }
    }
} 