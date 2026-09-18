using CesiumForUnity;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements.Experimental;

public class AllignmentManager : MonoBehaviour
{

    [Header("AR Camera & Origin")]
    [SerializeField] private Transform _positionOffsetTransform;
    [SerializeField] private Transform _rotationOffsetTransform;
    [SerializeField] private Transform _arCameraTransform;

    [Header("Target Center (Bamberg)")]
    [SerializeField] private Cesium3DTileset _tileset;
    [SerializeField] private float _dummyThresholdKm = 1.0f;
    [SerializeField] private float _dummyLat = 49.89185661f;
    [SerializeField] private float _dummyLon = 10.88662952f;

    [Header("Compass Alignment (Heading)")]
    [SerializeField] private bool _alignToCompass = true;
    [SerializeField] private float _compassSmoothness = 2f;

    [Header("Joystick & Manual Rotation Controls")]
    [SerializeField] private float _joystickSpeed = 0.5f;
    [SerializeField] private float _manualRotationSpeed = 5.0f;

    [Header("Ground Alignment")]
    [SerializeField] private bool _alignToGround = true;
    [SerializeField] private float _cameraHeightAboveGround = 1.65f;
    [SerializeField] private float _raycastStartHeight = 200f;
    [SerializeField] private float _alignmentSmoothness = 5f;
    [SerializeField] private LayerMask _groundLayerMask;

    [Header("Managers")]
    [SerializeField] public EvaluationManager _evaluationManager;
    [SerializeField] public LocationManager _LocationManager;
    [SerializeField] public GPSConnector _GPSConnector;
    [SerializeField] public CameraAlignmentManager _cameraAlignmentManager;
    

    private Vector3 _pureJoystickOffsetMeters = Vector3.zero;
    private float _buttonRotationDirection = 0f;
    private float rotationMutiplier = 0.01f;
    private bool isRotatesManually = false;
    private float _rotationOffset = 0f;
    private float _capturedCompassHeading = 0f;
    private CameraAlignmentManager.CameraPose _cameraAlignmentOffset = new CameraAlignmentManager.CameraPose(Vector3.zero, new Quaternion(0,0,0,0));

    private float _efficiency;

    private GPSConnector.Cords _currentCords;
    private Vector3 _cityPosition = new Vector3(0, 0, 0);
    void Start()
    {
        if (_positionOffsetTransform != null && _rotationOffsetTransform != null)
        {
            //Debugger.DisplayVar("rot", () => _rotationOffsetTransform.rotation.eulerAngles.y.ToString("F1") + "°");
            //Debugger.DisplayVector("pos", () => _positionOffsetTransform.position);
        }
        else
            Debugger.Log("Missed rotationOffset or positionOffset in AllignmentManeger", Debugger.MsgType.Error);


        ResetPosition();
    }

    // Update is called once per frame
    void Update()
    {
        Vector2 joystickInput = Vector2.zero;
        if (Gamepad.current != null) joystickInput = Gamepad.current.leftStick.ReadValue();

        if (joystickInput != Vector2.zero && _arCameraTransform != null)
        {
            Vector3 camForward = new Vector3(_arCameraTransform.forward.x, 0f, _arCameraTransform.forward.z).normalized;
            Vector3 camRight = new Vector3(_arCameraTransform.right.x, 0f, _arCameraTransform.right.z).normalized;
            Vector3 moveDirection = (camForward * joystickInput.y + camRight * joystickInput.x).normalized;

            Vector3 translationDelta = -moveDirection * _joystickSpeed * Time.deltaTime;
            _pureJoystickOffsetMeters += translationDelta;
            setPosition();
        }
        if (isRotatesManually && _arCameraTransform != null)
        {
            float step = _buttonRotationDirection * _manualRotationSpeed * rotationMutiplier * Time.deltaTime * 10;
            if (step != 0)
            {
                _rotationOffset += step;
                setRotation();
            }
            if (rotationMutiplier < 8f)
                rotationMutiplier += 0.1f * Time.deltaTime;
        }
        else
        {
            rotationMutiplier = 0.01f;
        }
    }
    private bool isCesiumLoaded = false;
    IEnumerator WaitCesiumLoading()
    {   
        isCesiumLoaded = false;
        if (_tileset != null)
        {
            float loaded = 0;
            while (loaded < 100f)
            {
                loaded = _tileset.ComputeLoadProgress();
                if (loaded > 95) isCesiumLoaded = true;
                Debugger.DisplayProgressBar("Cesium Loading", (int)loaded, 100);
                yield return new WaitForSeconds(1.0f);
            }
            isCesiumLoaded = true;
            Debugger.HideProgressBar("Cesium Loading");
        }
    }
    IEnumerator LocationAlignmentRoutine()
    {
        Debugger.Log("Position reset. Starting GPS allignment...");
        isCesiumLoaded = false;
        float _efficiencyStartTime = Time.time;

        bool _activateLocationCallbackRecieved = false;
        bool _isActivationSuccessful = false;

        StartCoroutine(_LocationManager.activateLocation(isSuccessfull =>
        {
            _activateLocationCallbackRecieved = true;
            _isActivationSuccessful = isSuccessfull;
        }));

        yield return new WaitUntil(() => _activateLocationCallbackRecieved);

        bool _getLocationCallbackRecieved = false;
        (GPSConnector.Cords deviceCords, bool isGPSfounded, float heading, bool isCompassAvailable) = _LocationManager.GetLocation();
        if (!isGPSfounded)
        {
            ActivateDummyMode("GPS not found. Forced Bamberg Center.", Debugger.MsgType.Error);
            yield break;
        }


        if (isCompassAvailable)
        {
            _capturedCompassHeading = heading;
            Debugger.Log($"Rotating sity to compass heading: {heading}");
            setRotation();
        }
        else
        {
            Debugger.Log("Compass failure", Debugger.MsgType.Error);
        }


        float distanceToTarget = _GPSConnector.distanceToBase();
        if (distanceToTarget > _dummyThresholdKm)
        {
            ActivateDummyMode($"Too far ({distanceToTarget:F2} km). Forced Bamberg Center.", Debugger.MsgType.Warn);
            _LocationManager.deactivateLocation();
        }
        else
        {
            _currentCords = new GPSConnector.Cords(deviceCords.lat, deviceCords.lon, deviceCords.alt); //save copy of gps cords
            Debugger.Log($"GPS linked successfull: ({deviceCords.lat}, {deviceCords.lon})", Debugger.MsgType.Success);
            MoveCityToCords(deviceCords);
            _LocationManager.deactivateLocation();
        }

        yield return new WaitForSeconds(1.0f);
        StartCoroutine(WaitCesiumLoading());
        yield return new WaitForSeconds(0.5f);
        yield return new WaitUntil(() => isCesiumLoaded);
        //AlignWithCamera();

        _efficiency = Time.time - _efficiencyStartTime;
        Debugger.Log($"GPS allignment complite! Took {(_efficiency):F2} seconds.", Debugger.MsgType.Success);
        
    }

    public void AlignWithCamera()
    {
        StartCoroutine(CameraAlignment());
    }
    public IEnumerator CameraAlignment()
    {
        bool isAligned = false;
        StartCoroutine(_cameraAlignmentManager.Align(callback =>
        {
            _cameraAlignmentOffset = callback;
            setPosition();
            setRotation();
            isAligned = true;
        }));
        yield return new WaitUntil(() => isAligned);
    } 

    public void StartRotateLeft()
    {
        isRotatesManually = true;
        _buttonRotationDirection = 1f;
    }

    public void StartRotateRight()
    {
        isRotatesManually = true;
        _buttonRotationDirection = -1f;
    }

    public void StopRotation()
    {
        isRotatesManually = false;
        _buttonRotationDirection = 0f;
    }

    public void ResetPosition()
    {
        StopAllCoroutines();
        _evaluationManager.ResetMetrics();
        _capturedCompassHeading = 0f;
        _currentCords = null;
        _rotationOffset = 0f;
        _pureJoystickOffsetMeters = Vector3.zero;
        StartCoroutine(LocationAlignmentRoutine());
        //StartCoroutine(RaycastLoopRoutine());
    }

    public void AcceptPosition()
    {
        Debugger.Log("Accept Position");

        GPSConnector.Cords realCords = _GPSConnector.FromUnityCords(_positionOffsetTransform.position);
        
        Debugger.Log("Real Cords: " +  realCords.ToString());
        float realHeading = _capturedCompassHeading - _rotationOffset;
        _evaluationManager.AcceptPosition(realCords, _currentCords, realHeading, _capturedCompassHeading, _efficiency);
        StartCoroutine(_LocationManager.MultipleMeasuresGPSAccuracy(realCords, 9, realHeading, _capturedCompassHeading));
    }


    private void ActivateDummyMode(string reason, Debugger.MsgType type)
    {
        Debugger.Log("Dummy Mode activated: " + reason, type);
        _currentCords = new GPSConnector.Cords(_dummyLat, _dummyLon, 0);
        MoveCityToCords(_currentCords);
    }
    /*
    IEnumerator RaycastLoopRoutine() { while (true) { AdjustAltitudeByRaycast(); yield return new WaitForSeconds(1f); } }

    private Coroutine _raycastCoroutine;
    void OnEnable() { if (_alignToGround) _raycastCoroutine = StartCoroutine(RaycastLoopRoutine()); }
    void OnDisable() { if (_raycastCoroutine != null) StopCoroutine(_raycastCoroutine); }
*/   
    private Vector3 AdjustAltitudeByRaycast(Vector3 position)
    {
        Vector3 rayOrigin = new Vector3(_arCameraTransform.position.x, _arCameraTransform.position.y + _raycastStartHeight, _arCameraTransform.position.z);
        Ray ray = new Ray(rayOrigin, Vector3.down);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, _raycastStartHeight * 2, _groundLayerMask))
        {
            float currentCameraHeightRelativeToRig = _arCameraTransform.position.y - _positionOffsetTransform.position.y;
            float targetRigY = hit.point.y - (-_cameraHeightAboveGround - currentCameraHeightRelativeToRig);

            position.y = targetRigY + _cameraAlignmentOffset.position.y;
        }
        return position;
    }
    private void setPosition()
    {
        var newPosition = _cityPosition - _pureJoystickOffsetMeters + _cameraAlignmentOffset.position;
        _positionOffsetTransform.position = AdjustAltitudeByRaycast(newPosition);
    }
    private void setRotation()
    {
        float angle = _capturedCompassHeading - _rotationOffset + _arCameraTransform.rotation.y + _cameraAlignmentOffset.ry;
        Quaternion rot = Quaternion.Euler(0f, angle, 0f);
        _rotationOffsetTransform.rotation = rot;
    }

    private void MoveCityToCords(GPSConnector.Cords cords)
    {
        Vector3 cityPosition = _GPSConnector.ToUnityCords(cords);
        cityPosition.y = _cityPosition.y;
        _cityPosition = cityPosition;
        Debugger.Log($"Moving player to GPS cords: ({cords.lat};{cords.lon}); ({_cityPosition.x};{_cityPosition.z})");
        setPosition();
    }
}
