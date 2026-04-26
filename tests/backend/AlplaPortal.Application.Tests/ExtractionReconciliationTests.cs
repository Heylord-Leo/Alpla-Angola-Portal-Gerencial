using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlplaPortal.Application.DTOs.Extraction;
using AlplaPortal.Application.Interfaces.Extraction;
using AlplaPortal.Application.Models.Configuration;
using AlplaPortal.Infrastructure.Services.Extraction;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlplaPortal.Application.Tests
{
    public class ExtractionReconciliationTests
    {
        private DocumentExtractionService CreateService(ExtractionResultDto expectedResult)
        {
            var mockProvider = new Mock<IDocumentExtractionProvider>();
            mockProvider.Setup(p => p.Name).Returns("OPENAI");
            mockProvider.Setup(p => p.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);

            var providers = new List<IDocumentExtractionProvider> { mockProvider.Object };

            var mockSettingsService = new Mock<IDocumentExtractionSettingsService>();
            mockSettingsService.Setup(s => s.GetEffectiveSettingsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentExtractionOptions
                {
                    IsEnabled = true,
                    DefaultProvider = "OPENAI",
                    OpenAi = new OpenAiSettings { Enabled = true, TimeoutSeconds = 30 }
                });

            return new DocumentExtractionService(providers, mockSettingsService.Object, new NullLogger<DocumentExtractionService>());
        }

        [Fact]
        public async Task Should_Not_Double_Apply_Summary_Discount_When_Line_Discounts_Already_Match()
        {
            // Arrange - The AD CERTO scenario
            var result = new ExtractionResultDto
            {
                Success = true,
                Header = new ExtractionHeaderDto
                {
                    Currency = "AOA",
                    DiscountAmount = 1000.00m // Summary discount
                },
                Items = new List<ExtractionLineItemDto>
                {
                    new ExtractionLineItemDto
                    {
                        Description = "BICHA FLEX",
                        Quantity = 1,
                        UnitPrice = 50000.00m,
                        DiscountAmount = 0m,
                        TotalPrice = 57000.00m // includes VAT for testing logic if needed, but not used by reconciliation
                    },
                    new ExtractionLineItemDto
                    {
                        Description = "MOLA C61945",
                        Quantity = 1,
                        UnitPrice = 10000.00m,
                        DiscountAmount = 1000.00m, // Line discount matches summary
                        TotalPrice = 10260.00m
                    }
                }
            };

            var service = CreateService(result);

            // Act
            var finalResult = await service.ExtractAsync(new MemoryStream(), "test.pdf");

            // Assert
            Assert.True(finalResult.Success);
            Assert.NotNull(finalResult.Header);
            // The global discount should be explicitly zeroed to prevent double-counting
            Assert.Equal(0m, finalResult.Header.DiscountAmount);
            // The line discount should remain untouched
            Assert.Equal(1000.00m, finalResult.Items[1].DiscountAmount);
        }

        [Fact]
        public async Task Should_Apply_Difference_As_Global_Discount_When_Summary_Discount_Exceeds_Line_Discounts()
        {
            // Arrange
            var result = new ExtractionResultDto
            {
                Success = true,
                Header = new ExtractionHeaderDto
                {
                    Currency = "AOA",
                    DiscountAmount = 3000.00m // Summary discount = 3000
                },
                Items = new List<ExtractionLineItemDto>
                {
                    new ExtractionLineItemDto
                    {
                        Description = "Item 1",
                        DiscountAmount = 1000.00m // Line discount = 1000
                    }
                }
            };

            var service = CreateService(result);

            // Act
            var finalResult = await service.ExtractAsync(new MemoryStream(), "test.pdf");

            // Assert
            // The difference (3000 - 1000 = 2000) should be set as the global discount
            Assert.Equal(2000.00m, finalResult.Header!.DiscountAmount);
            Assert.Equal(1000.00m, finalResult.Items[0].DiscountAmount);
        }

        [Fact]
        public async Task Should_Handle_No_Line_Discounts_But_Summary_Discount_Exists()
        {
            // Arrange
            var result = new ExtractionResultDto
            {
                Success = true,
                Header = new ExtractionHeaderDto
                {
                    Currency = "AOA",
                    DiscountAmount = 5000.00m // Summary discount = 5000
                },
                Items = new List<ExtractionLineItemDto>
                {
                    new ExtractionLineItemDto
                    {
                        Description = "Item 1",
                        DiscountAmount = 0m
                    }
                }
            };

            var service = CreateService(result);

            // Act
            var finalResult = await service.ExtractAsync(new MemoryStream(), "test.pdf");

            // Assert
            // Line discounts are 0, so global discount remains 5000
            Assert.Equal(5000.00m, finalResult.Header!.DiscountAmount);
        }

        [Fact]
        public async Task Should_Handle_Rounding_Differences_Within_Tolerance()
        {
            // Arrange
            var result = new ExtractionResultDto
            {
                Success = true,
                Header = new ExtractionHeaderDto
                {
                    Currency = "AOA",
                    DiscountAmount = 1000.50m // Summary discount with rounding
                },
                Items = new List<ExtractionLineItemDto>
                {
                    new ExtractionLineItemDto
                    {
                        Description = "Item 1",
                        DiscountAmount = 1000.00m // Line discount
                    }
                }
            };

            var service = CreateService(result);

            // Act
            var finalResult = await service.ExtractAsync(new MemoryStream(), "test.pdf");

            // Assert
            // Difference is 0.50, which is <= 1.0m tolerance for AOA, so global discount should be zeroed
            Assert.Equal(0m, finalResult.Header!.DiscountAmount);
        }

        [Fact]
        public async Task Should_Zero_Global_Discount_If_Summary_Less_Than_Line_Discounts()
        {
            // Arrange
            var result = new ExtractionResultDto
            {
                Success = true,
                Header = new ExtractionHeaderDto
                {
                    Currency = "AOA",
                    DiscountAmount = 500.00m // Summary discount
                },
                Items = new List<ExtractionLineItemDto>
                {
                    new ExtractionLineItemDto
                    {
                        Description = "Item 1",
                        DiscountAmount = 1000.00m // Line discount
                    }
                }
            };

            var service = CreateService(result);

            // Act
            var finalResult = await service.ExtractAsync(new MemoryStream(), "test.pdf");

            // Assert
            // Summary is less than sum of line discounts, this is an anomaly. We set global to 0.
            Assert.Equal(0m, finalResult.Header!.DiscountAmount);
        }
    }
}
