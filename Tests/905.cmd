@rae 905s/nineOfive.rad src/v.rad 905s/driveway.rad 905s/bathroom.rad
@if errorlevel 1 goto error
@exit /b
:error
@echo Error occured.
@pause