using BootstrapBlazor.OnlyServer1.Models;

namespace BootstrapBlazor.OnlyServer1.Helpers {
    public class MyHelper {
        public static string GetUID() {
            return System.Environment.UserName;
        }

        public static string GetDrawingType(Orders order) {
            if (order.IsReactor) {
                return "C08";
            }else if (order.Power > 2 && order.Power <= 10){
                return "C02";
            } else if (order.Power > 10 && order.Power < 40) {
                return "C03";
            }
            else if (order.Power >= 40 && order.Power < 100) {
                return "C04";
            }
            else if (order.Power >= 100 && order.Power < 200) {
                return "C05";
            }
            else if (order.Power >= 200 && order.Power < 400) {
                return "C06";
            }
            else if (order.Power >= 400) {
                return "C07";
            }
            else {
                return null;
            }
        }

        public static int GetMinDrawingNumber(string drawingType) {
            switch (drawingType) {
                case "C02": {
                        return 0;
                    }
                case "C03": {
                        return 0;
                    }
                case "C04": {
                        return 17000;
                    }
                case "C05": {
                        return 34000;
                    }
                case "C06": {
                        return 22893;
                    }
                case "C07": {
                        return 2992;
                    }
                case "C08": {
                        return 0;
                    }
                default: {
                        return 0;
                    }
            }
        }
    }
}
