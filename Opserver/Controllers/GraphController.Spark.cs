using StackExchange.Opserver.Data.Dashboard;
using StackExchange.Opserver.Data.SQL;
using StackExchange.Opserver.Helpers;
using StackExchange.Opserver.Models;
using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.UI.DataVisualization.Charting;

namespace StackExchange.Opserver.Controllers
{
    public partial class GraphController
    {
        private const int SparkHours = 24;

        [OutputCache(Duration = 120, VaryByParam = "id", VaryByContentEncoding = "gzip;deflate", VaryByCustom = "highDPI")]
        [Route("graph/cpu/spark"), AlsoAllow(Roles.InternalRequest)]
        public async Task<ActionResult> CPUSpark(string id)
        {
            var chart = GetSparkChart();
            var dataPoints = await DashboardData.GetCPUUtilization(id,
                start: DateTime.UtcNow.AddHours(-SparkHours),
                end: null,
                pointCount: (int)chart.Width.Value);

            if (dataPoints == null) return ContentNotFound();

            var area = GetSparkChartArea(100);
            var avgCPU = GetSparkSeries("Avg Load");
            chart.Series.Add(avgCPU);

            foreach (var mp in dataPoints)
            {
                if (mp.AvgLoad.HasValue)
                    avgCPU.Points.Add(new DataPoint(mp.DateTime.ToOADate(), mp.AvgLoad.Value));
            }

            chart.ChartAreas.Add(area);

            return chart.ToResult();
        }

        [OutputCache(Duration = 120, VaryByParam = "id", VaryByContentEncoding = "gzip;deflate", VaryByCustom = "highDPI")]
        [Route("graph/memory/spark"), AlsoAllow(Roles.InternalRequest)]
        public async Task<ActionResult> MemorySpark(string id)
        {
            var chart = GetSparkChart();
            var dataPoints = (await DashboardData.GetMemoryUtilization(id,
                start: DateTime.UtcNow.AddHours(-SparkHours),
                end: null,
                pointCount: (int)chart.Width.Value)).ToList();

            var maxMem = dataPoints.Max(mp => mp.TotalMemory).GetValueOrDefault();
            var maxGB = (int)Math.Ceiling(maxMem / _gb);

            var area = GetSparkChartArea(maxMem + (maxGB / 8) * _gb);
            var used = GetSparkSeries("Used");
            chart.Series.Add(used);

            foreach (var mp in dataPoints)
            {
                if (mp.AvgMemoryUsed.HasValue)
                    used.Points.Add(new DataPoint(mp.DateTime.ToOADate(), mp.AvgMemoryUsed.Value));
            }
            chart.ChartAreas.Add(area);

            return chart.ToResult();
        }

        [OutputCache(Duration = 120, VaryByParam = "id", VaryByContentEncoding = "gzip;deflate", VaryByCustom = "highDPI")]
        [Route("graph/network/spark"), AlsoAllow(Roles.InternalRequest)]
        public async Task<ActionResult> NetworkSpark(string id)
        {
            var node = DashboardData.GetNodeById(id);
            if (node == null) return ContentNotFound();

            var chart = GetSparkChart();
            var pointTasks = node.PrimaryInterfaces.Select(
                ni => DashboardData.GetInterfaceUtilization(ni.Id,
                    start: DateTime.UtcNow.AddHours(-SparkHours),
                    end: null,
                    pointCount: (int)chart.Width.Value));
            var dataPoints = (await Task.WhenAll(pointTasks)).SelectMany(t => t).OrderBy(t => t.DateTime);

            var area = GetSparkChartArea();
            var series = GetSparkSeries("Total");
            series.ChartType = SeriesChartType.StackedArea;
            chart.Series.Add(series);

            foreach (var np in dataPoints)
            {
                series.Points.Add(new DataPoint(np.DateTime.ToOADate(), np.InAvgBps.GetValueOrDefault(0) + np.OutAvgBps.GetValueOrDefault(0)));
            }
            chart.DataManipulator.Group("SUM", 2, IntervalType.Minutes, series);

            chart.ChartAreas.Add(area);

            return chart.ToResult();
        }

        [OutputCache(Duration = 120, VaryByParam = "id", VaryByContentEncoding = "gzip;deflate", VaryByCustom = "highDPI")]
        [Route("graph/interface/{direction}/spark")]
        public async Task<ActionResult> InterfaceOutSpark(string direction, string id)
        {
            var chart = GetSparkChart();
            var dataPoints = (await DashboardData.GetInterfaceUtilization(id,
                start: DateTime.UtcNow.AddHours(-SparkHours),
                end: null,
                pointCount: (int)chart.Width.Value))
                .OrderBy(dp => dp.DateTime);

            var area = GetSparkChartArea();
            var series = GetSparkSeries("Bytes");
            chart.Series.Add(series);

            foreach (var np in dataPoints)
            {
                series.Points.Add(new DataPoint(np.DateTime.ToOADate(),
                                                direction == "out"
                                                    ? np.OutAvgBps.GetValueOrDefault(0)
                                                    : np.InAvgBps.GetValueOrDefault(0)));
            }
            chart.ChartAreas.Add(area);

            return chart.ToResult();
        }

        [OutputCache(Duration = 120, VaryByParam = "node", VaryByContentEncoding = "gzip;deflate", VaryByCustom = "highDPI")]
        [Route("graph/sql/cpu/spark")]
        public ActionResult SQLCPUSpark(string node)
        {
            var instance = SQLInstance.Get(node);
            if (instance == null)
                return ContentNotFound("SQLNode not found with name = '" + node + "'");

            var dataPoints = instance.CPUHistoryLastHour;
            var chart = new Chart();
            var area = new ChartArea();
            area.AxisX.Enabled = AxisEnabled.False;
            area.AxisY.Enabled = AxisEnabled.False;
            area.AxisY.Maximum = 100;

            chart.ChartAreas.Add(area);

            var series = new Series();
            foreach (var item in dataPoints.Data)
            {
                series.Points.AddXY(item.EventTime.ToOADate(), item.ProcessUtilization);
            }
            series.Label = "";
            series.Font = new Font("Segoe UI", 8.0f, FontStyle.Bold);
            series.ChartType = SeriesChartType.Area;
            chart.Series.Add(series);
            return chart.ToResult();
        }

        private static ChartArea GetSparkChartArea(double? max = null, int? daysAgo = null, bool noLine = false)
        {
            var area = new ChartArea("area")
            {
                BackColor = Color.Transparent,
                Position = new ElementPosition(0, 0, 100, 100),
                InnerPlotPosition = new ElementPosition(0, 0, 100, 100),
                AxisY =
                {
                    MaximumAutoSize = 100,
                    LabelStyle = { Enabled = false },
                    MajorGrid = { Enabled = false },
                    MajorTickMark = { Enabled = false },
                    LineColor = Color.Transparent,
                    LineDashStyle = ChartDashStyle.Dot,
                },
                AxisX =
                {
                    MaximumAutoSize = 100,
                    LabelStyle = { Enabled = false },
                    Maximum = DateTime.UtcNow.ToOADate(),
                    Minimum = DateTime.UtcNow.AddDays(-(daysAgo ?? 1)).ToOADate(),
                    MajorGrid = { Enabled = false },
                    LineColor = ColorTranslator.FromHtml("#a3c0d7")
                }
            };

            if (max.HasValue)
                area.AxisY.Maximum = max.Value;
            if (noLine)
                area.AxisX.LineColor = Color.Transparent;

            return area;
        }

        private static Series GetSparkSeries(string name, Color? color = null)
        {
            color = color ?? Color.SteelBlue;
            return new Series(name)
            {
                ChartType = SeriesChartType.Area,
                XValueType = ChartValueType.DateTime,
                Color = ColorTranslator.FromHtml("#c6d5e2"),
                EmptyPointStyle = { Color = Color.Transparent, BackSecondaryColor = Color.Transparent }
            };
        }
    }
}