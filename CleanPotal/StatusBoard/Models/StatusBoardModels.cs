using System;
using System.ComponentModel;

namespace CleanPotal.StatusBoard.Models
{
    // -----------------------------------------------------------------------
    // 1. MaterialLogisticsBoard (자재물류 일정 현황 - 천안사업장)
    // -----------------------------------------------------------------------
    public class MaterialLogisticsRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public long Id { get; set; }

        private string _boardDate = "";
        public string BoardDate
        {
            get => _boardDate;
            set { _boardDate = value; OnPropChanged(nameof(BoardDate)); }
        }

        private string _personName = "";
        public string PersonName
        {
            get => _personName;
            set { _personName = value; OnPropChanged(nameof(PersonName)); }
        }

        private string _amDestination = "";
        public string AmDestination
        {
            get => _amDestination;
            set { _amDestination = value; OnPropChanged(nameof(AmDestination)); }
        }

        private string _amVehicle = "";
        public string AmVehicle
        {
            get => _amVehicle;
            set { _amVehicle = value; OnPropChanged(nameof(AmVehicle)); }
        }

        private string _pmDestination = "";
        public string PmDestination
        {
            get => _pmDestination;
            set { _pmDestination = value; OnPropChanged(nameof(PmDestination)); }
        }

        private string _pmVehicle = "";
        public string PmVehicle
        {
            get => _pmVehicle;
            set { _pmVehicle = value; OnPropChanged(nameof(PmVehicle)); }
        }

        private string _memo = "";
        public string Memo
        {
            get => _memo;
            set { _memo = value; OnPropChanged(nameof(Memo)); }
        }

        public int OrderNo { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    // 자재물류 고정 인원(담당자) 로스터 — 날짜와 무관하게 유지, 순서 지정
    public class MaterialLogisticsMember
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int OrderNo { get; set; }
    }

    // -----------------------------------------------------------------------
    // 2. ProductionBoard (생산 현황판)
    // -----------------------------------------------------------------------

    /// <summary>포장 수량 (주간/야간)</summary>
    public class ProductionPackaging : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public long Id { get; set; }

        private string _boardDate = "";
        public string BoardDate
        {
            get => _boardDate;
            set { _boardDate = value; OnPropChanged(nameof(BoardDate)); }
        }

        private string _shift = "";
        public string Shift
        {
            get => _shift;
            set { _shift = value; OnPropChanged(nameof(Shift)); }
        }

        private string _shiftRound = "";
        public string ShiftRound
        {
            get => _shiftRound;
            set { _shiftRound = value; OnPropChanged(nameof(ShiftRound)); }
        }

        private string _region = "";
        public string Region
        {
            get => _region;
            set { _region = value; OnPropChanged(nameof(Region)); }
        }

        private int _outerQty;
        public int OuterQty
        {
            get => _outerQty;
            set { _outerQty = value; OnPropChanged(nameof(OuterQty)); }
        }

        private int _innerQty;
        public int InnerQty
        {
            get => _innerQty;
            set { _innerQty = value; OnPropChanged(nameof(InnerQty)); }
        }

        private int _boatQty;
        public int BoatQty
        {
            get => _boatQty;
            set { _boatQty = value; OnPropChanged(nameof(BoatQty)); }
        }

        private int _accQty;
        public int AccQty
        {
            get => _accQty;
            set { _accQty = value; OnPropChanged(nameof(AccQty)); }
        }

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>팀 인원 (장팀/팀)</summary>
    public class ProductionTeamMember : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public long Id { get; set; }

        private string _teamType = "";
        public string TeamType
        {
            get => _teamType;
            set { _teamType = value; OnPropChanged(nameof(TeamType)); }
        }

        private string _memberName = "";
        public string MemberName
        {
            get => _memberName;
            set { _memberName = value; OnPropChanged(nameof(MemberName)); }
        }

        private string _experience = "";
        public string Experience
        {
            get => _experience;
            set { _experience = value; OnPropChanged(nameof(Experience)); }
        }

        private string _phoneNumber = "";
        public string PhoneNumber
        {
            get => _phoneNumber;
            set { _phoneNumber = value; OnPropChanged(nameof(PhoneNumber)); }
        }

        public int OrderNo { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    // -----------------------------------------------------------------------
    // 3. DongtanLogisticsBoard (동탄 물류 현황판)
    // -----------------------------------------------------------------------

    /// <summary>배차표</summary>
    public class DongtanDispatch : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public long Id { get; set; }

        private string _boardDate = "";
        public string BoardDate
        {
            get => _boardDate;
            set { _boardDate = value; OnPropChanged(nameof(BoardDate)); }
        }

        private string _driverName = "";
        public string DriverName
        {
            get => _driverName;
            set { _driverName = value; OnPropChanged(nameof(DriverName)); }
        }

        private string _vehicleInfo = "";
        public string VehicleInfo
        {
            get => _vehicleInfo;
            set { _vehicleInfo = value; OnPropChanged(nameof(VehicleInfo)); }
        }

        private string _amRoute = "";
        public string AmRoute
        {
            get => _amRoute;
            set { _amRoute = value; OnPropChanged(nameof(AmRoute)); }
        }

        private string _pmRoute = "";
        public string PmRoute
        {
            get => _pmRoute;
            set { _pmRoute = value; OnPropChanged(nameof(PmRoute)); }
        }

        private string _routeGroup = "";
        public string RouteGroup
        {
            get => _routeGroup;
            set { _routeGroup = value; OnPropChanged(nameof(RouteGroup)); }
        }

        public int OrderNo { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>반입/반출 물량</summary>
    public class DongtanQuantity : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public long Id { get; set; }

        private string _boardDate = "";
        public string BoardDate
        {
            get => _boardDate;
            set { _boardDate = value; OnPropChanged(nameof(BoardDate)); }
        }

        private string _timeSlot = "";
        public string TimeSlot
        {
            get => _timeSlot;
            set { _timeSlot = value; OnPropChanged(nameof(TimeSlot)); }
        }

        private string _direction = "";
        public string Direction
        {
            get => _direction;
            set { _direction = value; OnPropChanged(nameof(Direction)); }
        }

        private string _region = "";
        public string Region
        {
            get => _region;
            set { _region = value; OnPropChanged(nameof(Region)); }
        }

        private int _outerQty;
        public int OuterQty
        {
            get => _outerQty;
            set { _outerQty = value; OnPropChanged(nameof(OuterQty)); }
        }

        private int _innerQty;
        public int InnerQty
        {
            get => _innerQty;
            set { _innerQty = value; OnPropChanged(nameof(InnerQty)); }
        }

        private int _boatQty;
        public int BoatQty
        {
            get => _boatQty;
            set { _boatQty = value; OnPropChanged(nameof(BoatQty)); }
        }

        private int _accQty;
        public int AccQty
        {
            get => _accQty;
            set { _accQty = value; OnPropChanged(nameof(AccQty)); }
        }

        private int _etcQty;
        public int EtcQty
        {
            get => _etcQty;
            set { _etcQty = value; OnPropChanged(nameof(EtcQty)); }
        }

        private string _etcMemo = "";
        public string EtcMemo
        {
            get => _etcMemo;
            set { _etcMemo = value; OnPropChanged(nameof(EtcMemo)); }
        }

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
